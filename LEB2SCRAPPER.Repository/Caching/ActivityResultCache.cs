using System.Collections.Concurrent;
using System.Diagnostics;
using LEB2SCRAPPER.Contracts.Repository;
using Microsoft.Extensions.Logging;
using ActivityModel = LEB2SCRAPPER.Entity.Models.Activity.Activity;

namespace LEB2SCRAPPER.Repository.Caching;

public sealed class ActivityResultCache : IActivityResultCache
{
    private readonly Dictionary<CacheKey, CacheEntry> _entries = new();
    private readonly ConcurrentDictionary<
        CacheKey,
        Lazy<Task<List<ActivityModel>>>> _inFlight = new();
    private readonly object _entryLock = new();
    private readonly ILogger<ActivityResultCache> _logger;
    private readonly ActivityResultCacheOptions _options;
    private readonly TimeProvider _timeProvider;

    public ActivityResultCache(
        ActivityResultCacheOptions options,
        TimeProvider timeProvider,
        ILogger<ActivityResultCache> logger)
    {
        if (options.AbsoluteTtlSeconds <= 0 || options.MaximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Activity cache settings must be greater than zero.");
        }

        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    internal int EntryCount
    {
        get
        {
            lock (_entryLock)
            {
                PruneExpiredEntries(_timeProvider.GetUtcNow());
                return _entries.Count;
            }
        }
    }

    internal int InFlightCount => _inFlight.Count;

    public async Task<List<ActivityModel>> GetActivitiesAsync(
        string clientKey,
        int userId,
        int classId,
        Func<CancellationToken, Task<List<ActivityModel>>> valueFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (string.IsNullOrWhiteSpace(clientKey))
        {
            throw new ArgumentException(
                "Opaque client key must be provided.",
                nameof(clientKey));
        }

        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (classId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(classId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        var key = new CacheKey(clientKey, userId, classId);

        if (TryGetValue(key, out var cachedActivities))
        {
            LogStatus("hit", startedAt);
            return cachedActivities;
        }

        var pendingValue = new Lazy<Task<List<ActivityModel>>>(
            () => CreateAndStoreAsync(
                key,
                valueFactory,
                cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var sharedValue = _inFlight.GetOrAdd(key, pendingValue);
        var cacheStatus = ReferenceEquals(sharedValue, pendingValue)
            ? "miss"
            : "coalesced";

        try
        {
            var activities = await sharedValue.Value.WaitAsync(cancellationToken);
            LogStatus(cacheStatus, startedAt);
            return activities.ToList();
        }
        catch (OperationCanceledException)
        {
            LogStatus($"{cacheStatus}-canceled", startedAt);
            throw;
        }
        catch
        {
            LogStatus($"{cacheStatus}-failed", startedAt);
            throw;
        }
    }

    private async Task<List<ActivityModel>> CreateAndStoreAsync(
        CacheKey key,
        Func<CancellationToken, Task<List<ActivityModel>>> valueFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var activities = await valueFactory(cancellationToken);
            var cachedActivities = activities.ToList();
            StoreValue(key, cachedActivities);
            return cachedActivities.ToList();
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private bool TryGetValue(
        CacheKey key,
        out List<ActivityModel> activities)
    {
        lock (_entryLock)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpiredEntries(now);

            if (_entries.TryGetValue(key, out var entry))
            {
                activities = entry.Activities.ToList();
                return true;
            }
        }

        activities = new List<ActivityModel>();
        return false;
    }

    private void StoreValue(
        CacheKey key,
        List<ActivityModel> activities)
    {
        lock (_entryLock)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpiredEntries(now);

            if (!_entries.ContainsKey(key)
                && _entries.Count >= _options.MaximumEntries)
            {
                var oldestKey = _entries
                    .MinBy(pair => pair.Value.CreatedAt)
                    .Key;
                _entries.Remove(oldestKey);
            }

            _entries[key] = new CacheEntry(
                activities,
                now,
                now.AddSeconds(_options.AbsoluteTtlSeconds));
        }
    }

    private void PruneExpiredEntries(DateTimeOffset now)
    {
        foreach (var key in _entries
                     .Where(pair => pair.Value.ExpiresAt <= now)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _entries.Remove(key);
        }
    }

    private void LogStatus(
        string cacheStatus,
        long startedAt)
    {
        _logger.LogInformation(
            "Activity cache lookup completed with status {CacheStatus} "
            + "in {CacheElapsedMilliseconds} ms.",
            cacheStatus,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private readonly record struct CacheKey(
        string ClientKey,
        int UserId,
        int ClassId);

    private sealed record CacheEntry(
        List<ActivityModel> Activities,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);
}
