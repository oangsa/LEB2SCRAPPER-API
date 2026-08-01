using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Repository.Caching;

namespace LEB2SCRAPPER.Tests.Repository;

public class StructuralScrapeCacheTests
{
    [Fact]
    public async Task Entries_AreIsolatedByClientAndSemester()
    {
        var timeProvider = new ManualTimeProvider();
        using var cache = CreateCache(timeProvider);
        var calls = 0;

        var firstClient = await cache.GetSemesterIdsAsync(
            "client-a",
            _ => Task.FromResult<List<int>?>([Interlocked.Increment(ref calls)]));
        var secondClient = await cache.GetSemesterIdsAsync(
            "client-b",
            _ => Task.FromResult<List<int>?>([Interlocked.Increment(ref calls)]));
        var firstSemester = await cache.GetClassesAsync(
            "client-a",
            10,
            _ => Task.FromResult<List<ClassInfo>?>(
                [new ClassInfo { Id = Interlocked.Increment(ref calls) }]));
        var secondSemester = await cache.GetClassesAsync(
            "client-a",
            11,
            _ => Task.FromResult<List<ClassInfo>?>(
                [new ClassInfo { Id = Interlocked.Increment(ref calls) }]));

        Assert.Equal([1], firstClient);
        Assert.Equal([2], secondClient);
        Assert.Equal(3, Assert.Single(firstSemester!).Id);
        Assert.Equal(4, Assert.Single(secondSemester!).Id);
    }

    [Fact]
    public async Task Entry_ExpiresAfterAbsoluteTtl()
    {
        var timeProvider = new ManualTimeProvider();
        using var cache = CreateCache(timeProvider, ttlSeconds: 60);
        var calls = 0;

        var first = await cache.GetSemesterIdsAsync(
            "client",
            _ => Task.FromResult<List<int>?>([Interlocked.Increment(ref calls)]));
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        var cached = await cache.GetSemesterIdsAsync(
            "client",
            _ => Task.FromResult<List<int>?>([Interlocked.Increment(ref calls)]));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var refreshed = await cache.GetSemesterIdsAsync(
            "client",
            _ => Task.FromResult<List<int>?>([Interlocked.Increment(ref calls)]));

        Assert.Equal([1], first);
        Assert.Equal([1], cached);
        Assert.Equal([2], refreshed);
    }

    [Fact]
    public async Task SuccessfulEmptyClassList_IsCached()
    {
        using var cache = CreateCache(new ManualTimeProvider());
        var calls = 0;

        var first = await cache.GetClassesAsync(
            "client",
            10,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<List<ClassInfo>?>([]);
            });
        var second = await cache.GetClassesAsync(
            "client",
            10,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<List<ClassInfo>?>([]);
            });

        Assert.Empty(first!);
        Assert.Empty(second!);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClassMembershipLookup_ReusesSameClientAndSemesterEntry()
    {
        using var cache = CreateCache(new ManualTimeProvider());
        var calls = 0;

        Task<List<ClassInfo>?> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<List<ClassInfo>?>(
                [new ClassInfo { Id = 10 }]);
        }

        var first = await cache.GetClassesAsync("client", 10, Factory);
        var second = await cache.GetClassesAsync("client", 10, Factory);

        Assert.Equal(1, calls);
        Assert.Equal([10], first!.Select(classInfo => classInfo.Id));
        Assert.Equal([10], second!.Select(classInfo => classInfo.Id));
    }

    [Fact]
    public async Task ExceptionsNullsAndCancellation_AreNotCached()
    {
        using var cache = CreateCache(new ManualTimeProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetSemesterIdsAsync(
                "exception-client",
                _ => throw new InvalidOperationException("Synthetic failure.")));
        var afterException = await cache.GetSemesterIdsAsync(
            "exception-client",
            _ => Task.FromResult<List<int>?>([1]));

        var nullResult = await cache.GetSemesterIdsAsync(
            "null-client",
            _ => Task.FromResult<List<int>?>(null));
        var afterNull = await cache.GetSemesterIdsAsync(
            "null-client",
            _ => Task.FromResult<List<int>?>([2]));

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.GetSemesterIdsAsync(
                "canceled-client",
                token => Task.FromCanceled<List<int>?>(token),
                cancellationSource.Token));
        var afterCancellation = await cache.GetSemesterIdsAsync(
            "canceled-client",
            _ => Task.FromResult<List<int>?>([3]));

        Assert.Equal([1], afterException);
        Assert.Null(nullResult);
        Assert.Equal([2], afterNull);
        Assert.Equal([3], afterCancellation);
    }

    [Fact]
    public async Task ConcurrentMisses_AreCoalesced()
    {
        using var cache = CreateCache(new ManualTimeProvider());
        var factoryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<List<int>?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            factoryStarted.TrySetResult();
            await releaseFactory.Task.WaitAsync(cancellationToken);
            return [10, 11];
        }

        var first = cache.GetSemesterIdsAsync("client", Factory);
        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = cache.GetSemesterIdsAsync("client", Factory);
        releaseFactory.TrySetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Equal([10, 11], results[0]);
        Assert.Equal([10, 11], results[1]);
        Assert.Equal(0, cache.KeyLockCount);
    }

    [Fact]
    public async Task ReturnedCollections_AreDefensiveCopies()
    {
        using var cache = CreateCache(new ManualTimeProvider());

        var semesterIds = await cache.GetSemesterIdsAsync(
            "client",
            _ => Task.FromResult<List<int>?>([10]));
        semesterIds!.Add(99);
        var cachedSemesterIds = await cache.GetSemesterIdsAsync(
            "client",
            _ => throw new InvalidOperationException());

        var classes = await cache.GetClassesAsync(
            "client",
            10,
            _ => Task.FromResult<List<ClassInfo>?>(
                [new ClassInfo { Id = 100, Name = "Original" }]));
        classes![0].Name = "Mutated";
        var cachedClasses = await cache.GetClassesAsync(
            "client",
            10,
            _ => throw new InvalidOperationException());

        Assert.Equal([10], cachedSemesterIds);
        Assert.Equal("Original", Assert.Single(cachedClasses!).Name);
    }

    [Fact]
    public async Task CapacityAndIdleKeyCleanup_AreBounded()
    {
        using var cache = CreateCache(
            new ManualTimeProvider(),
            maximumEntries: 2);

        for (var index = 0; index < 3; index++)
        {
            await cache.GetSemesterIdsAsync(
                $"client-{index}",
                _ => Task.FromResult<List<int>?>([index]));
        }

        Assert.Equal(2, cache.EntryCount);
        Assert.Equal(0, cache.KeyLockCount);
    }

    private static StructuralScrapeCache CreateCache(
        TimeProvider timeProvider,
        int ttlSeconds = 60,
        int maximumEntries = 10_000)
    {
        return new StructuralScrapeCache(
            new StructuralScrapeCacheOptions
            {
                AbsoluteTtlSeconds = ttlSeconds,
                MaximumEntries = maximumEntries
            },
            timeProvider);
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
