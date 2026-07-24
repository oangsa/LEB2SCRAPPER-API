using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Infrastructure.Contracts.Alerting;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using Microsoft.Extensions.Logging;

namespace LEB2SCRAPPER.Infrastructure.Outbound;

public sealed class OutboundRequestGate :
    IOutboundRequestGate,
    IOutboundRequestStatusReader,
    IDisposable
{
    private readonly IFailureAlerter _failureAlerter;
    private readonly ILogger<OutboundRequestGate> _logger;
    private readonly OutboundRequestGateOptions _options;
    private readonly SemaphoreSlim _requestSemaphore;
    private readonly TimeProvider _timeProvider;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, ClientLimiter> _clientLimiters = new();
    private readonly Dictionary<string, FailureState> _failureStates = new();
    private readonly Dictionary<IncidentKey, StructuralIncident> _structuralIncidents = new();
    private bool _disposed;

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

    internal int ClientLimiterCount
    {
        get
        {
            lock (_stateLock)
            {
                return _clientLimiters.Count;
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(
        OutboundRequestContext context,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ValidateContext(context);

        var now = _timeProvider.GetUtcNow();
        ThrowIfBackoffActive(context, now);
        var clientLimiter = ReserveClientLimiter(context.ClientKey, now);
        var clientPermitAcquired = false;
        var globalPermitAcquired = false;
        PendingStructuralAlert? pendingAlert = null;

        try
        {
            await clientLimiter.Semaphore.WaitAsync(cancellationToken);
            clientPermitAcquired = true;
            await _requestSemaphore.WaitAsync(cancellationToken);
            globalPermitAcquired = true;

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
                now = _timeProvider.GetUtcNow();
                RecordBackoff(context, now);
                pendingAlert = RecordStructuralFailure(
                    context,
                    exception.FailureShape,
                    now);
                throw;
            }
            catch (TransientLeb2Exception)
            {
                RecordBackoff(context, _timeProvider.GetUtcNow());
                throw;
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
        finally
        {
            if (globalPermitAcquired)
            {
                _requestSemaphore.Release();
            }

            ReleaseClientLimiter(
                context.ClientKey,
                clientLimiter,
                clientPermitAcquired);
        }
    }

    public OutboundRequestStatusSnapshot GetSnapshot()
    {
        var now = _timeProvider.GetUtcNow();

        lock (_stateLock)
        {
            ThrowIfDisposed();
            PruneExpiredState(now);
            var endpoints = Leb2OutboundEndpoints.All
                .Select(endpoint =>
                {
                    var retryAt = _failureStates.TryGetValue(endpoint, out var state)
                        && state.RetryAt > now
                            ? state.RetryAt
                            : (DateTimeOffset?)null;
                    var retryAfterSeconds = retryAt.HasValue
                        ? Math.Max(
                            1,
                            (int)Math.Ceiling((retryAt.Value - now).TotalSeconds))
                        : 0;

                    return new OutboundEndpointStatus(
                        endpoint,
                        retryAt,
                        retryAfterSeconds);
                })
                .ToList();

            return new OutboundRequestStatusSnapshot(now, endpoints);
        }
    }

    public void Dispose()
    {
        List<SemaphoreSlim> clientSemaphores;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            clientSemaphores = _clientLimiters.Values
                .Select(limiter => limiter.Semaphore)
                .ToList();
            _clientLimiters.Clear();
            _failureStates.Clear();
            _structuralIncidents.Clear();
        }

        _requestSemaphore.Dispose();

        foreach (var semaphore in clientSemaphores)
        {
            semaphore.Dispose();
        }
    }

    private static void ValidateOptions(OutboundRequestGateOptions options)
    {
        if (options.MaxConcurrentRequests <= 0
            || options.MaxConcurrentRequestsPerClient <= 0
            || options.MaxQueuedRequestsPerClient < 0
            || options.ClientThrottleRetryAfterSeconds <= 0
            || options.BaseBackoffSeconds <= 0
            || options.MaxBackoffMinutes <= 0
            || options.FailureResetMinutes <= 0
            || options.StructuralFailureThreshold <= 0
            || options.StructuralFailureWindowMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Outbound request gate settings are invalid.");
        }
    }

    private static void ValidateContext(OutboundRequestContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Endpoint)
            || string.IsNullOrWhiteSpace(context.ClientKey))
        {
            throw new ArgumentException(
                "Outbound endpoint and opaque client key must be provided.",
                nameof(context));
        }
    }

    private ClientLimiter ReserveClientLimiter(
        string clientKey,
        DateTimeOffset now)
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (!_clientLimiters.TryGetValue(clientKey, out var limiter))
            {
                limiter = new ClientLimiter(_options.MaxConcurrentRequestsPerClient);
                _clientLimiters[clientKey] = limiter;
            }

            var maximumOutstandingRequests =
                _options.MaxConcurrentRequestsPerClient
                + _options.MaxQueuedRequestsPerClient;

            if (limiter.OutstandingRequests >= maximumOutstandingRequests)
            {
                if (limiter.OutstandingRequests == 0)
                {
                    _clientLimiters.Remove(clientKey);
                    limiter.Semaphore.Dispose();
                }

                throw new OutboundClientThrottleException(
                    now.AddSeconds(_options.ClientThrottleRetryAfterSeconds));
            }

            limiter.OutstandingRequests++;
            return limiter;
        }
    }

    private void ReleaseClientLimiter(
        string clientKey,
        ClientLimiter limiter,
        bool permitAcquired)
    {
        if (permitAcquired)
        {
            limiter.Semaphore.Release();
        }

        var disposeSemaphore = false;

        lock (_stateLock)
        {
            limiter.OutstandingRequests--;

            if (limiter.OutstandingRequests == 0
                && _clientLimiters.TryGetValue(clientKey, out var current)
                && ReferenceEquals(current, limiter))
            {
                _clientLimiters.Remove(clientKey);
                disposeSemaphore = true;
            }
        }

        if (disposeSemaphore)
        {
            limiter.Semaphore.Dispose();
        }
    }

    private void ThrowIfBackoffActive(
        OutboundRequestContext context,
        DateTimeOffset now)
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            PruneExpiredState(now);

            if (_failureStates.TryGetValue(context.Endpoint, out var state)
                && state.RetryAt > now)
            {
                throw new OutboundRequestBackoffException(state.RetryAt);
            }
        }
    }

    private void RecordBackoff(
        OutboundRequestContext context,
        DateTimeOffset now)
    {
        lock (_stateLock)
        {
            PruneExpiredState(now);
            _failureStates.TryGetValue(context.Endpoint, out var state);

            var failureResetWindow = TimeSpan.FromMinutes(_options.FailureResetMinutes);
            var consecutiveFailures = state is null
                || now - state.LastFailureAt > failureResetWindow
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
            var window = TimeSpan.FromMinutes(
                _options.StructuralFailureWindowMinutes);
            var incidentKey = new IncidentKey(context.Endpoint, failureShape);

            if (!_structuralIncidents.TryGetValue(incidentKey, out var incident))
            {
                incident = new StructuralIncident();
                _structuralIncidents[incidentKey] = incident;
            }

            incident.Failures.RemoveAll(failure => now - failure.OccurredAt > window);

            if (incident.Failures.Count == 0)
            {
                incident.AlertSent = false;
            }

            incident.Failures.Add(new StructuralFailureOccurrence(
                now,
                context.ClientKey));

            var distinctClientCount = incident.Failures
                .Select(failure => failure.ClientKey)
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (incident.AlertSent
                || incident.AlertDeliveryInProgress
                || incident.Failures.Count < _options.StructuralFailureThreshold
                || distinctClientCount < 2)
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
                    incident.Failures.Min(failure => failure.OccurredAt),
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

        var structuralWindow = TimeSpan.FromMinutes(
            _options.StructuralFailureWindowMinutes);

        foreach (var pair in _structuralIncidents.ToList())
        {
            pair.Value.Failures.RemoveAll(
                failure => now - failure.OccurredAt > structuralWindow);

            if (pair.Value.Failures.Count == 0
                && !pair.Value.AlertDeliveryInProgress)
            {
                _structuralIncidents.Remove(pair.Key);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record FailureState(
        int ConsecutiveFailures,
        DateTimeOffset LastFailureAt,
        DateTimeOffset RetryAt);

    private sealed record IncidentKey(string Endpoint, string FailureShape);

    private sealed record StructuralFailureOccurrence(
        DateTimeOffset OccurredAt,
        string ClientKey);

    private sealed record PendingStructuralAlert(
        StructuralIncident Incident,
        StructuralFailureAlert Alert);

    private sealed class ClientLimiter
    {
        public ClientLimiter(int maximumConcurrency)
        {
            Semaphore = new SemaphoreSlim(
                maximumConcurrency,
                maximumConcurrency);
        }

        public SemaphoreSlim Semaphore { get; }

        public int OutstandingRequests { get; set; }
    }

    private sealed class StructuralIncident
    {
        public List<StructuralFailureOccurrence> Failures { get; } = new();

        public bool AlertDeliveryInProgress { get; set; }

        public bool AlertSent { get; set; }
    }
}
