using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Semester;

namespace LEB2SCRAPPER.Repository.Caching;

public sealed class StructuralScrapeCache : IStructuralScrapeCache, IDisposable
{
    private readonly Dictionary<CacheKey, CacheEntry> _entries = new();
    private readonly Dictionary<CacheKey, KeyLock> _keyLocks = new();
    private readonly object _stateLock = new();
    private readonly StructuralScrapeCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public StructuralScrapeCache(
        StructuralScrapeCacheOptions options,
        TimeProvider timeProvider)
    {
        if (options.AbsoluteTtlSeconds <= 0 || options.MaximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Structural scrape cache settings must be greater than zero.");
        }

        _options = options;
        _timeProvider = timeProvider;
    }

    internal int EntryCount
    {
        get
        {
            lock (_stateLock)
            {
                PruneExpiredEntries(_timeProvider.GetUtcNow());
                return _entries.Count;
            }
        }
    }

    internal int KeyLockCount
    {
        get
        {
            lock (_stateLock)
            {
                return _keyLocks.Count;
            }
        }
    }

    public Task<List<SemesterInfo>?> GetSemestersAsync(
        string clientKey,
        Func<CancellationToken, Task<List<SemesterInfo>?>> valueFactory,
        CancellationToken cancellationToken = default)
    {
        var key = new CacheKey(CacheValueKind.Semesters, clientKey, null);

        return GetOrCreateAsync(
            key,
            valueFactory,
            CloneSemesters,
            cancellationToken);
    }

    public Task<List<ClassInfo>?> GetClassesAsync(
        string clientKey,
        int semesterId,
        Func<CancellationToken, Task<List<ClassInfo>?>> valueFactory,
        CancellationToken cancellationToken = default)
    {
        if (semesterId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(semesterId));
        }

        var key = new CacheKey(CacheValueKind.Classes, clientKey, semesterId);

        return GetOrCreateAsync(
            key,
            valueFactory,
            CloneClasses,
            cancellationToken);
    }

    public void Dispose()
    {
        List<SemaphoreSlim> semaphores;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entries.Clear();
            semaphores = _keyLocks.Values
                .Select(keyLock => keyLock.Semaphore)
                .ToList();
            _keyLocks.Clear();
        }

        foreach (var semaphore in semaphores)
        {
            semaphore.Dispose();
        }
    }

    private async Task<T?> GetOrCreateAsync<T>(
        CacheKey key,
        Func<CancellationToken, Task<T?>> valueFactory,
        Func<T, T> clone,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (string.IsNullOrWhiteSpace(key.ClientKey))
        {
            throw new ArgumentException(
                "Opaque client key must be provided.",
                nameof(key));
        }

        if (TryGetValue(key, clone, out T? cachedValue))
        {
            return cachedValue;
        }

        var keyLock = ReserveKeyLock(key);
        var lockAcquired = false;

        try
        {
            await keyLock.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;

            if (TryGetValue(key, clone, out cachedValue))
            {
                return cachedValue;
            }

            var createdValue = await valueFactory(cancellationToken);

            if (createdValue is null)
            {
                return null;
            }

            StoreValue(key, clone(createdValue));
            return clone(createdValue);
        }
        finally
        {
            ReleaseKeyLock(key, keyLock, lockAcquired);
        }
    }

    private bool TryGetValue<T>(
        CacheKey key,
        Func<T, T> clone,
        out T? value)
        where T : class
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            var now = _timeProvider.GetUtcNow();
            PruneExpiredEntries(now);

            if (_entries.TryGetValue(key, out var entry)
                && entry.Value is T typedValue)
            {
                value = clone(typedValue);
                return true;
            }
        }

        value = null;
        return false;
    }

    private KeyLock ReserveKeyLock(CacheKey key)
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (!_keyLocks.TryGetValue(key, out var keyLock))
            {
                keyLock = new KeyLock();
                _keyLocks[key] = keyLock;
            }

            keyLock.ReferenceCount++;
            return keyLock;
        }
    }

    private void ReleaseKeyLock(
        CacheKey key,
        KeyLock keyLock,
        bool lockAcquired)
    {
        if (lockAcquired)
        {
            keyLock.Semaphore.Release();
        }

        var disposeSemaphore = false;

        lock (_stateLock)
        {
            keyLock.ReferenceCount--;

            if (keyLock.ReferenceCount == 0
                && _keyLocks.TryGetValue(key, out var current)
                && ReferenceEquals(current, keyLock))
            {
                _keyLocks.Remove(key);
                disposeSemaphore = true;
            }
        }

        if (disposeSemaphore)
        {
            keyLock.Semaphore.Dispose();
        }
    }

    private void StoreValue(CacheKey key, object value)
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
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
                value,
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static List<ClassInfo> CloneClasses(List<ClassInfo> classes)
    {
        return classes
            .Select(classInfo => new ClassInfo
            {
                Id = classInfo.Id,
                Name = classInfo.Name
            })
            .ToList();
    }

    private static List<SemesterInfo> CloneSemesters(List<SemesterInfo> semesters)
    {
        return semesters
            .Select(semesterInfo => new SemesterInfo
            {
                Id = semesterInfo.Id,
                Name = semesterInfo.Name
            })
            .ToList();
    }

    private enum CacheValueKind
    {
        Semesters,
        Classes
    }

    private sealed record CacheKey(
        CacheValueKind ValueKind,
        string ClientKey,
        int? SemesterId);

    private sealed record CacheEntry(
        object Value,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);

    private sealed class KeyLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }
}
