using System.Collections.Concurrent;
using System.Text.Json;
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
    public async Task StructuralFailures_FromOneClient_DoNotAlert()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);
        var context = Context("classes", "client-a");

        await AssertStructuralFailureAsync(gate, context);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, context);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, context);

        Assert.Empty(alerter.Alerts);
    }

    [Fact]
    public async Task StructuralFailures_AcrossTwoClients_AlertOnce()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);

        await AssertStructuralFailureAsync(gate, Context("classes", "client-a"));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-a"));
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-b"));

        var alert = await alerter.FirstAlert.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("classes", alert.Endpoint);
        Assert.Equal("classes.class_cards", alert.FailureShape);
        Assert.Equal(3, alert.FailureCount);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-b"));
        await Task.Delay(50);

        Assert.Single(alerter.Alerts);
    }

    [Fact]
    public async Task DifferentEndpointsOrShapes_DoNotCorroborate()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);

        await AssertStructuralFailureAsync(
            gate,
            Context("classes", "client-a"),
            "classes.class_cards");
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(
            gate,
            Context("classes", "client-b"),
            "classes.class_card_values");
        await AssertStructuralFailureAsync(
            gate,
            Context("semesters", "client-b"),
            "classes.class_cards");

        Assert.Empty(alerter.Alerts);
    }

    [Fact]
    public async Task ExpiredStructuralFailures_DoNotCount()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);

        await AssertStructuralFailureAsync(gate, Context("classes", "client-a"));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-a"));
        timeProvider.Advance(TimeSpan.FromMinutes(16));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-b"));

        Assert.Empty(alerter.Alerts);
    }

    [Fact]
    public async Task AlertDeliveryFailure_RemainsRetryable()
    {
        var alerter = new FailOnceFailureAlerter();
        var logger = new SignalingLogger();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider, logger: logger);

        await AssertStructuralFailureAsync(gate, Context("classes", "client-a"));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-a"));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-b"));
        await logger.AlertFailureLogged.WaitAsync(TimeSpan.FromSeconds(5));

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await AssertStructuralFailureAsync(gate, Context("classes", "client-b"));

        var deliveredAlert = await alerter.DeliveredAlert.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.Equal(2, alerter.Attempts);
        Assert.Equal(4, deliveredAlert.FailureCount);
    }

    [Fact]
    public async Task AlertPayload_DoesNotContainClientIdentity()
    {
        var alerter = new RecordingFailureAlerter();
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(alerter, timeProvider);
        const string clientIdentity = "opaque-client-that-must-not-leak";

        await AssertStructuralFailureAsync(
            gate,
            Context("classes", clientIdentity));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(
            gate,
            Context("classes", clientIdentity));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await AssertStructuralFailureAsync(
            gate,
            Context("classes", "second-client"));

        var alert = await alerter.FirstAlert.WaitAsync(TimeSpan.FromSeconds(5));
        var serializedAlert = JsonSerializer.Serialize(alert);

        Assert.DoesNotContain(clientIdentity, serializedAlert);
        Assert.DoesNotContain("Client", serializedAlert);
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesGlobalMaximum()
    {
        using var gate = CreateGate(
            new RecordingFailureAlerter(),
            new ManualTimeProvider(),
            maximumConcurrency: 4);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fourStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;

        var tasks = Enumerable.Range(0, 8)
            .Select(index => gate.ExecuteAsync(
                Context("activities", $"client-{index}"),
                async _ =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);

                    if (current == 4)
                    {
                        fourStarted.TrySetResult();
                    }

                    await release.Task;
                    Interlocked.Decrement(ref active);
                    return index;
                }))
            .ToArray();

        await fourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, Volatile.Read(ref maximumActive));

        release.TrySetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(4, maximumActive);
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesClientMaximum()
    {
        using var gate = CreateGate(
            new RecordingFailureAlerter(),
            new ManualTimeProvider());
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var twoStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;

        var tasks = Enumerable.Range(0, 4)
            .Select(index => gate.ExecuteAsync(
                Context("activities", "same-client"),
                async _ =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);

                    if (current == 2)
                    {
                        twoStarted.TrySetResult();
                    }

                    await release.Task;
                    Interlocked.Decrement(ref active);
                    return index;
                }))
            .ToArray();

        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.Equal(2, Volatile.Read(ref maximumActive));

        release.TrySetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(2, maximumActive);
    }

    [Fact]
    public async Task NoisyClient_DoesNotBlockAnotherClient()
    {
        using var gate = CreateGate(
            new RecordingFailureAlerter(),
            new ManualTimeProvider());
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var noisyStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var quietStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var noisyActive = 0;

        var noisyTasks = Enumerable.Range(0, 6)
            .Select(index => gate.ExecuteAsync(
                Context("activities", "noisy-client"),
                async _ =>
                {
                    if (Interlocked.Increment(ref noisyActive) == 2)
                    {
                        noisyStarted.TrySetResult();
                    }

                    await release.Task;
                    return index;
                }))
            .ToArray();

        await noisyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var quietTask = gate.ExecuteAsync(
            Context("activities", "quiet-client"),
            _ =>
            {
                quietStarted.TrySetResult();
                return Task.FromResult(42);
            });

        await quietStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, await quietTask);

        release.TrySetResult();
        await Task.WhenAll(noisyTasks);
    }

    [Fact]
    public async Task QueueBeyondClientLimit_IsRejected()
    {
        using var gate = CreateGate(
            new RecordingFailureAlerter(),
            new ManualTimeProvider());
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var acceptedTasks = Enumerable.Range(0, 10)
            .Select(index => gate.ExecuteAsync(
                Context("activities", "same-client"),
                async _ =>
                {
                    await release.Task;
                    return index;
                }))
            .ToArray();

        await Assert.ThrowsAsync<OutboundClientThrottleException>(() =>
            gate.ExecuteAsync(
                Context("activities", "same-client"),
                _ => Task.FromResult(42)));

        release.TrySetResult();
        await Task.WhenAll(acceptedTasks);
    }

    [Fact]
    public async Task PermitsAndIdleClientState_AreReleasedAfterFailure()
    {
        using var gate = CreateGate(
            new RecordingFailureAlerter(),
            new ManualTimeProvider());
        var context = Context("activities", "same-client");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.ExecuteAsync<int>(
                context,
                _ => throw new InvalidOperationException("Synthetic failure.")));

        var result = await gate.ExecuteAsync(
            context,
            _ => Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Equal(0, gate.ClientLimiterCount);
    }

    [Fact]
    public async Task HealthSnapshot_ReportsEveryEndpointAndActiveBackoff()
    {
        var timeProvider = new ManualTimeProvider();
        using var gate = CreateGate(
            new RecordingFailureAlerter(),
            timeProvider);

        await Assert.ThrowsAsync<TransientLeb2Exception>(() =>
            gate.ExecuteAsync<int>(
                Context(Leb2OutboundEndpoints.Activities, "client-a"),
                _ => throw new TransientLeb2Exception("Temporary failure.")));

        var snapshot = gate.GetSnapshot();

        Assert.Equal(timeProvider.GetUtcNow(), snapshot.ObservedAt);
        Assert.Equal(Leb2OutboundEndpoints.All, snapshot.Endpoints.Select(e => e.Name));
        var activities = Assert.Single(
            snapshot.Endpoints,
            endpoint => endpoint.Name == Leb2OutboundEndpoints.Activities);
        Assert.NotNull(activities.RetryAt);
        Assert.Equal(1, activities.RetryAfterSeconds);
        Assert.All(
            snapshot.Endpoints.Where(endpoint => endpoint.Name != activities.Name),
            endpoint =>
            {
                Assert.Null(endpoint.RetryAt);
                Assert.Equal(0, endpoint.RetryAfterSeconds);
            });
    }

    private static OutboundRequestGate CreateGate(
        IFailureAlerter alerter,
        TimeProvider timeProvider,
        int maximumConcurrency = 4,
        ILogger<OutboundRequestGate>? logger = null)
    {
        return new OutboundRequestGate(
            new OutboundRequestGateOptions
            {
                MaxConcurrentRequests = maximumConcurrency,
                MaxConcurrentRequestsPerClient = 2,
                MaxQueuedRequestsPerClient = 8,
                ClientThrottleRetryAfterSeconds = 1,
                BaseBackoffSeconds = 1,
                MaxBackoffMinutes = 1,
                FailureResetMinutes = 15,
                StructuralFailureThreshold = 3,
                StructuralFailureWindowMinutes = 15
            },
            alerter,
            timeProvider,
            logger ?? NullLogger<OutboundRequestGate>.Instance);
    }

    private static OutboundRequestContext Context(
        string endpoint,
        string clientKey)
    {
        return new OutboundRequestContext(endpoint, clientKey);
    }

    private static Task AssertStructuralFailureAsync(
        IOutboundRequestGate gate,
        OutboundRequestContext context,
        string failureShape = "classes.class_cards")
    {
        return Assert.ThrowsAsync<StructuralParseException>(() =>
            gate.ExecuteAsync<int>(
                context,
                _ => throw new StructuralParseException(
                    failureShape,
                    "Unexpected response shape.")));
    }

    private static void UpdateMaximum(ref int maximum, int current)
    {
        int observed;

        do
        {
            observed = Volatile.Read(ref maximum);

            if (current <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, current, observed) != observed);
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
        private DateTimeOffset _utcNow =
            new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);

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
