using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Infrastructure.Contracts.Alerting;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using Microsoft.Extensions.Logging;

namespace LEB2SCRAPPER.Infrastructure.Outbound;

public sealed class OutboundRequestGate : IOutboundRequestGate, IDisposable
{
    private readonly IFailureAlerter _failureAlerter;
    private readonly ILogger<OutboundRequestGate> _logger;
    private readonly OutboundRequestGateOptions _options;
    private readonly SemaphoreSlim _requestSemaphore;
    private readonly TimeProvider _timeProvider;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, FailureState> _failureStates = new();
    private readonly Dictionary<IncidentKey, StructuralIncident> _structuralIncidents = new();

    public OutboundRequestGate(
        OutboundRequestGateOptions options,
        IFailureAlerter failureAlerter,
        TimeProvider timeProvider,
        ILogger<OutboundRequestGate> logger)
    {
        ValidateOptions(options);

        _options = options;
        _failureAlerter = failureAlerter;
        _timeProvider = timeProvider;
        _logger = logger;
        _requestSemaphore = new SemaphoreSlim(
            options.MaxConcurrentRequests,
            options.MaxConcurrentRequests);
    }

    public async Task<T> ExecuteAsync<T>(
        OutboundRequestContext context,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ThrowIfBackoffActive(context, _timeProvider.GetUtcNow());

        PendingStructuralAlert? pendingAlert = null;

        try
        {
            await _requestSemaphore.WaitAsync(cancellationToken);

            try
            {
                ThrowIfBackoffActive(context, _timeProvider.GetUtcNow());

                try
                {
                    var result = await operation(cancellationToken);
                    RecordSuccess(context);
                    return result;
                }
                catch (SessionExpiredException)
                {
                    ClearFailureState(context);
                    throw;
                }
                catch (StructuralParseException exception)
                {
                    var now = _timeProvider.GetUtcNow();
                    RecordBackoff(context, now);
                    pendingAlert = RecordStructuralFailure(context, exception.FailureShape, now);
                    throw;
                }
                catch (TransientLeb2Exception)
                {
                    RecordBackoff(context, _timeProvider.GetUtcNow());
                    throw;
                }
            }
            finally
            {
                _requestSemaphore.Release();
            }
        }
        catch (StructuralParseException)
        {
            if (pendingAlert is not null)
            {
                QueueAlertDelivery(pendingAlert);
            }

            throw;
        }
    }

    public void Dispose()
    {
        _requestSemaphore.Dispose();
    }

    private static void ValidateOptions(OutboundRequestGateOptions options)
    {
        if (options.MaxConcurrentRequests <= 0
            || options.BaseBackoffSeconds <= 0
            || options.MaxBackoffMinutes <= 0
            || options.FailureResetMinutes <= 0
            || options.StructuralFailureThreshold <= 0
            || options.StructuralFailureWindowMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Outbound request gate settings must be greater than zero.");
        }
    }

    private void ThrowIfBackoffActive(OutboundRequestContext context, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            PruneExpiredState(now);

            if (_failureStates.TryGetValue(context.Endpoint, out var state)
                && state.RetryAt > now)
            {
                throw new OutboundRequestBackoffException(state.RetryAt);
            }
        }
    }

    private void RecordBackoff(OutboundRequestContext context, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            PruneExpiredState(now);
            _failureStates.TryGetValue(context.Endpoint, out var state);

            var failureResetWindow = TimeSpan.FromMinutes(_options.FailureResetMinutes);
            var consecutiveFailures = state is null || now - state.LastFailureAt > failureResetWindow
                ? 1
                : state.ConsecutiveFailures + 1;
            var delay = CalculateBackoff(consecutiveFailures);

            _failureStates[context.Endpoint] = new FailureState(
                consecutiveFailures,
                now,
                now.Add(delay));
        }
    }

    private TimeSpan CalculateBackoff(int consecutiveFailures)
    {
        var exponent = Math.Min(consecutiveFailures - 1, 20);
        var delaySeconds = _options.BaseBackoffSeconds * Math.Pow(2, exponent);
        var maxDelaySeconds = TimeSpan.FromMinutes(_options.MaxBackoffMinutes).TotalSeconds;

        return TimeSpan.FromSeconds(Math.Min(delaySeconds, maxDelaySeconds));
    }

    private PendingStructuralAlert? RecordStructuralFailure(
        OutboundRequestContext context,
        string failureShape,
        DateTimeOffset now)
    {
        lock (_stateLock)
        {
            PruneExpiredState(now);
            var window = TimeSpan.FromMinutes(_options.StructuralFailureWindowMinutes);

            foreach (var key in _structuralIncidents.Keys
                         .Where(key => key.Endpoint == context.Endpoint && key.FailureShape != failureShape)
                         .ToList())
            {
                _structuralIncidents.Remove(key);
            }

            var incidentKey = new IncidentKey(context.Endpoint, failureShape);

            if (!_structuralIncidents.TryGetValue(incidentKey, out var incident))
            {
                incident = new StructuralIncident();
                _structuralIncidents[incidentKey] = incident;
            }

            incident.Failures.RemoveAll(failure => now - failure > window);

            if (incident.Failures.Count == 0)
            {
                incident.AlertSent = false;
            }

            incident.Failures.Add(now);

            if (incident.AlertSent
                || incident.AlertDeliveryInProgress
                || incident.Failures.Count < _options.StructuralFailureThreshold)
            {
                return null;
            }

            incident.AlertDeliveryInProgress = true;

            return new PendingStructuralAlert(
                incident,
                new StructuralFailureAlert(
                    context.Endpoint,
                    failureShape,
                    incident.Failures.Count,
                    incident.Failures.Min(),
                    now));
        }
    }

    private void RecordSuccess(OutboundRequestContext context)
    {
        lock (_stateLock)
        {
            _failureStates.Remove(context.Endpoint);

            foreach (var key in _structuralIncidents.Keys
                         .Where(key => key.Endpoint == context.Endpoint)
                         .ToList())
            {
                _structuralIncidents.Remove(key);
            }
        }
    }

    private void ClearFailureState(OutboundRequestContext context)
    {
        lock (_stateLock)
        {
            _failureStates.Remove(context.Endpoint);
        }
    }

    private void QueueAlertDelivery(PendingStructuralAlert pendingAlert)
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            _ = Task.Run(() => TrySendAlertAsync(pendingAlert));
            return;
        }

        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => TrySendAlertAsync(pendingAlert));
        }
    }

    private async Task TrySendAlertAsync(PendingStructuralAlert pendingAlert)
    {
        try
        {
            await _failureAlerter.NotifyStructuralFailureAsync(
                pendingAlert.Alert,
                CancellationToken.None);

            lock (_stateLock)
            {
                pendingAlert.Incident.AlertSent = true;
                pendingAlert.Incident.AlertDeliveryInProgress = false;
            }
        }
        catch (Exception exception)
        {
            lock (_stateLock)
            {
                pendingAlert.Incident.AlertDeliveryInProgress = false;
            }

            _logger.LogError(
                "Structural-failure alert delivery failed with {ExceptionType}.",
                exception.GetType().Name);
        }
    }

    private void PruneExpiredState(DateTimeOffset now)
    {
        var failureResetWindow = TimeSpan.FromMinutes(_options.FailureResetMinutes);

        foreach (var endpoint in _failureStates
                     .Where(pair => pair.Value.RetryAt <= now
                         && now - pair.Value.LastFailureAt > failureResetWindow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _failureStates.Remove(endpoint);
        }

        var structuralWindow = TimeSpan.FromMinutes(_options.StructuralFailureWindowMinutes);

        foreach (var pair in _structuralIncidents.ToList())
        {
            pair.Value.Failures.RemoveAll(failure => now - failure > structuralWindow);

            if (pair.Value.Failures.Count == 0
                && !pair.Value.AlertDeliveryInProgress)
            {
                _structuralIncidents.Remove(pair.Key);
            }
        }
    }

    private sealed record FailureState(
        int ConsecutiveFailures,
        DateTimeOffset LastFailureAt,
        DateTimeOffset RetryAt);

    private sealed record IncidentKey(string Endpoint, string FailureShape);

    private sealed record PendingStructuralAlert(
        StructuralIncident Incident,
        StructuralFailureAlert Alert);

    private sealed class StructuralIncident
    {
        public List<DateTimeOffset> Failures { get; } = new();
        public bool AlertDeliveryInProgress { get; set; }
        public bool AlertSent { get; set; }
    }
}
