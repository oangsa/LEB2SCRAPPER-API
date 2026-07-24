using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Repository.Caching;
using Microsoft.Extensions.Logging.Abstractions;

namespace LEB2SCRAPPER.Tests.Repository;

public class ActivityResultCacheTests
{
    [Fact]
    public async Task Entries_AreIsolatedBySessionUserAndClass()
    {
        var cache = CreateCache(new ManualTimeProvider());
        var calls = 0;

        var firstSession = await GetAsync(cache, "client-a", 1, 10, NextValue);
        var secondSession = await GetAsync(cache, "client-b", 1, 10, NextValue);
        var secondUser = await GetAsync(cache, "client-a", 2, 10, NextValue);
        var secondClass = await GetAsync(cache, "client-a", 1, 11, NextValue);

        Assert.Equal(1, Assert.Single(firstSession).Id);
        Assert.Equal(2, Assert.Single(secondSession).Id);
        Assert.Equal(3, Assert.Single(secondUser).Id);
        Assert.Equal(4, Assert.Single(secondClass).Id);

        Task<List<Activity>> NextValue(CancellationToken _)
        {
            return Task.FromResult(
                new List<Activity>
                {
                    new() { Id = Interlocked.Increment(ref calls) }
                });
        }
    }

    [Fact]
    public async Task SuccessfulEmptyResult_IsCachedUntilAbsoluteTtl()
    {
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(timeProvider, ttlSeconds: 30);
        var calls = 0;

        Task<List<Activity>> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new List<Activity>());
        }

        var first = await GetAsync(cache, "client", 1, 10, Factory);
        timeProvider.Advance(TimeSpan.FromSeconds(29));
        var cached = await GetAsync(cache, "client", 1, 10, Factory);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var refreshed = await GetAsync(cache, "client", 1, 10, Factory);

        Assert.Empty(first);
        Assert.Empty(cached);
        Assert.Empty(refreshed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailuresAndCancellation_AreNotCached()
    {
        var cache = CreateCache(new ManualTimeProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GetAsync(
                cache,
                "failure-client",
                1,
                10,
                _ => throw new InvalidOperationException("Synthetic failure.")));
        var afterFailure = await GetAsync(
            cache,
            "failure-client",
            1,
            10,
            _ => Task.FromResult(new List<Activity>
            {
                new() { Id = 1 }
            }));

        using var cancellationSource = new CancellationTokenSource();
        var canceled = GetAsync(
            cache,
            "canceled-client",
            1,
            10,
            async token =>
            {
                await cancellationSource.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new List<Activity>();
            },
            cancellationSource.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        var afterCancellation = await GetAsync(
            cache,
            "canceled-client",
            1,
            10,
            _ => Task.FromResult(new List<Activity>
            {
                new() { Id = 2 }
            }));

        Assert.Equal(1, Assert.Single(afterFailure).Id);
        Assert.Equal(2, Assert.Single(afterCancellation).Id);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task ConcurrentMisses_AreCoalesced()
    {
        var cache = CreateCache(new ManualTimeProvider());
        var factoryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<List<Activity>> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            factoryStarted.TrySetResult();
            await releaseFactory.Task.WaitAsync(cancellationToken);
            return [new Activity { Id = 10 }];
        }

        var first = GetAsync(cache, "client", 1, 10, Factory);
        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = GetAsync(cache, "client", 1, 10, Factory);
        releaseFactory.TrySetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Equal(10, Assert.Single(results[0]).Id);
        Assert.Equal(10, Assert.Single(results[1]).Id);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task CapacityAndReturnedCollections_AreBoundedAndIndependent()
    {
        var cache = CreateCache(
            new ManualTimeProvider(),
            maximumEntries: 2);

        var first = await GetAsync(
            cache,
            "client-0",
            1,
            10,
            _ => Task.FromResult(new List<Activity>
            {
                new() { Id = 1 }
            }));
        first.Clear();
        var cachedFirst = await GetAsync(
            cache,
            "client-0",
            1,
            10,
            _ => throw new InvalidOperationException());

        for (var index = 1; index < 3; index++)
        {
            await GetAsync(
                cache,
                $"client-{index}",
                1,
                10,
                _ => Task.FromResult(new List<Activity>
                {
                    new() { Id = index + 1 }
                }));
        }

        Assert.Equal(1, Assert.Single(cachedFirst).Id);
        Assert.Equal(2, cache.EntryCount);
    }

    private static Task<List<Activity>> GetAsync(
        ActivityResultCache cache,
        string clientKey,
        int userId,
        int classId,
        Func<CancellationToken, Task<List<Activity>>> valueFactory,
        CancellationToken cancellationToken = default)
    {
        return cache.GetActivitiesAsync(
            clientKey,
            userId,
            classId,
            valueFactory,
            cancellationToken);
    }

    private static ActivityResultCache CreateCache(
        TimeProvider timeProvider,
        int ttlSeconds = 30,
        int maximumEntries = 2_000)
    {
        return new ActivityResultCache(
            new ActivityResultCacheOptions
            {
                AbsoluteTtlSeconds = ttlSeconds,
                MaximumEntries = maximumEntries
            },
            timeProvider,
            NullLogger<ActivityResultCache>.Instance);
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
