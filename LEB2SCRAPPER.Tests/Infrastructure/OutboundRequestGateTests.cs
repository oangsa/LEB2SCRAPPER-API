using System.Collections.Concurrent;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Infrastructure.Contracts.Alerting;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Infrastructure.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LEB2SCRAPPER.Tests.Infrastructure;

public class OutboundRequestGateTests
{
    [Fact]
    public async Task StructuralFailures_AtThreshold_TriggerOneAlert()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);
        var context = new OutboundRequestContext("classes");

        await AssertStructuralFailureAsync(gate, context);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, context);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, context);

        var alert = await alerter.FirstAlert.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("classes", alert.Endpoint);
        Assert.Equal("classes.class_cards", alert.FailureShape);
        Assert.Equal(3, alert.FailureCount);
        Assert.Single(alerter.Alerts);
    }

    [Fact]
    public async Task StructuralFailures_BelowThreshold_DoNotAlert()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);
        var context = new OutboundRequestContext("classes");

        await AssertStructuralFailureAsync(gate, context);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, context);

        Assert.Empty(alerter.Alerts);
    }

    [Fact]
    public async Task TransientFailure_BacksOffEndpointThenRecoversWithoutAlerting()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);
        var context = new OutboundRequestContext("activities");
        var operationCalls = 0;

        await Assert.ThrowsAsync<TransientLeb2Exception>(() =>
            gate.ExecuteAsync<int>(
                context,
                _ => throw new TransientLeb2Exception("Temporary failure.")));

        await Assert.ThrowsAsync<OutboundRequestBackoffException>(() =>
            gate.ExecuteAsync(
                context,
                _ =>
                {
                    operationCalls++;
                    return Task.FromResult(42);
                }));

        Assert.Equal(0, operationCalls);

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        var result = await gate.ExecuteAsync(
            context,
            _ =>
            {
                operationCalls++;
                return Task.FromResult(42);
            });

        Assert.Equal(42, result);
        Assert.Equal(1, operationCalls);
        Assert.Empty(alerter.Alerts);
    }

    [Fact]
    public async Task SessionExpiry_DoesNotBackoffOrAlert()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);
        var context = new OutboundRequestContext("semesters");

        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            gate.ExecuteAsync<int>(
                context,
                _ => throw new SessionExpiredException()));

        var result = await gate.ExecuteAsync(
            context,
            _ => Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Empty(alerter.Alerts);
    }

    [Fact]
    public async Task FailedAlertDelivery_IsRetriedAfterNextStructuralFailure()
    {
        var alerter = new FailOnceFailureAlerter();
        var logger = new SignalingLogger();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(
            alerter,
            timeProvider,
            structuralFailureThreshold: 1,
            logger: logger);
        var context = new OutboundRequestContext("classes");

        await AssertStructuralFailureAsync(gate, context);
        await logger.AlertFailureLogged.WaitAsync(TimeSpan.FromSeconds(5));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, context);

        var deliveredAlert = await alerter.DeliveredAlert.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, alerter.Attempts);
        Assert.Equal(2, deliveredAlert.FailureCount);
    }

    [Fact]
    public async Task StructuralFailure_DoesNotWaitForAlertDelivery()
    {
        var alerter = new BlockingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(
            alerter,
            timeProvider,
            structuralFailureThreshold: 1);
        var context = new OutboundRequestContext("classes");

        await AssertStructuralFailureAsync(gate, context)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await alerter.Started.WaitAsync(TimeSpan.FromSeconds(5));

        alerter.Release();
        await alerter.Completed.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static OutboundRequestGate CreateGate(
        IFailureAlerter alerter,
        TimeProvider timeProvider,
        int structuralFailureThreshold = 3,
        ILogger<OutboundRequestGate>? logger = null)
    {
        return new OutboundRequestGate(
            new OutboundRequestGateOptions
            {
                MaxConcurrentRequests = 2,
                BaseBackoffSeconds = 1,
                MaxBackoffMinutes = 1,
                FailureResetMinutes = 15,
                StructuralFailureThreshold = structuralFailureThreshold,
                StructuralFailureWindowMinutes = 15
            },
            alerter,
            timeProvider,
            logger ?? NullLogger<OutboundRequestGate>.Instance);
    }

    private static Task AssertStructuralFailureAsync(
        IOutboundRequestGate gate,
        OutboundRequestContext context)
    {
        return Assert.ThrowsAsync<StructuralParseException>(() =>
            gate.ExecuteAsync<int>(
                context,
                _ => throw new StructuralParseException(
                    "classes.class_cards",
                    "Unexpected class shape.")));
    }

    private sealed class RecordingFailureAlerter : IFailureAlerter
    {
        private readonly TaskCompletionSource<StructuralFailureAlert> _firstAlert =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<StructuralFailureAlert> Alerts { get; } = new();
        public Task<StructuralFailureAlert> FirstAlert => _firstAlert.Task;

        public Task NotifyStructuralFailureAsync(
            StructuralFailureAlert alert,
            CancellationToken cancellationToken = default)
        {
            Alerts.Enqueue(alert);
            _firstAlert.TrySetResult(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnceFailureAlerter : IFailureAlerter
    {
        private readonly TaskCompletionSource<StructuralFailureAlert> _deliveredAlert =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);
        public Task<StructuralFailureAlert> DeliveredAlert => _deliveredAlert.Task;

        public Task NotifyStructuralFailureAsync(
            StructuralFailureAlert alert,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new InvalidOperationException("Synthetic SMTP failure.");
            }

            _deliveredAlert.TrySetResult(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingFailureAlerter : IFailureAlerter
    {
        private readonly TaskCompletionSource _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completed => _completed.Task;
        public Task Started => _started.Task;

        public async Task NotifyStructuralFailureAsync(
            StructuralFailureAlert alert,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task;
            _completed.TrySetResult();
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class SignalingLogger : ILogger<OutboundRequestGate>
    {
        private readonly TaskCompletionSource _alertFailureLogged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AlertFailureLogged => _alertFailureLogged.Task;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                _alertFailureLogged.TrySetResult();
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }
    }
}
