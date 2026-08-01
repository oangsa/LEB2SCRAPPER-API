using System.Collections.Concurrent;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Service;
using LEB2SCRAPPER.Service.Master;
using Microsoft.Extensions.Logging.Abstractions;

namespace LEB2SCRAPPER.Tests.Service;

public class ActivityServiceTests
{
    [Theory]
    [InlineData(0, 10, "fake-session")]
    [InlineData(123, 0, "fake-session")]
    [InlineData(123, 10, "")]
    public async Task GetActivitiesBySemesterAsync_RejectsInvalidInput(
        int userId,
        int semesterId,
        string token)
    {
        var service = CreateService(
            (_, _, _) => Task.FromResult<List<ClassInfo>?>([]),
            (_, _, _, _) => Task.FromResult(new List<Activity>()));

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.GetActivitiesBySemesterAsync(
                userId,
                semesterId,
                token));
    }

    [Fact]
    public async Task GetActivitiesBySemesterAsync_DiscoversOnceAndOrdersByClass()
    {
        var classDiscoveryCalls = 0;
        var activityCalls = new ConcurrentBag<int>();
        var service = CreateService(
            (semesterId, token, _) =>
            {
                Interlocked.Increment(ref classDiscoveryCalls);
                Assert.Equal(10, semesterId);
                Assert.Equal("fake-session", token);

                return Task.FromResult<List<ClassInfo>?>(
                [
                    new ClassInfo { Id = 3 },
                    new ClassInfo { Id = 1 },
                    new ClassInfo { Id = 2 },
                    new ClassInfo { Id = 1 },
                    new ClassInfo { Id = 0 },
                    new ClassInfo { Id = -1 }
                ]);
            },
            (userId, classId, token, _) =>
            {
                Assert.Equal(123, userId);
                Assert.Equal("fake-session", token);
                activityCalls.Add(classId);

                return Task.FromResult(new List<Activity>
                {
                    new() { Id = classId * 10 + 2, ClassId = classId },
                    new() { Id = classId * 10 + 1, ClassId = classId }
                });
            });

        var result = await service.GetActivitiesBySemesterAsync(
            123,
            10,
            "fake-session");

        Assert.Equal(1, classDiscoveryCalls);
        Assert.Equal([1, 2, 3], activityCalls.Order());
        Assert.Equal([1, 1, 2, 2, 3, 3], result.Select(activity => activity.ClassId));
        Assert.Equal([12, 11, 22, 21, 32, 31], result.Select(activity => activity.Id));
    }

    [Fact]
    public async Task GetSemesterSnapshotAsync_OrdersClassesAndIncludesEmptyClasses()
    {
        var service = CreateService(
            (_, _, _) => Task.FromResult<List<ClassInfo>?>(
            [
                new ClassInfo { Id = 30, Name = "Third" },
                new ClassInfo { Id = 10, Name = "First" },
                new ClassInfo { Id = 20, Name = "Second" }
            ]),
            (_, classId, _, _) => Task.FromResult(
                classId == 20
                    ? new List<Activity>()
                    :
                    [
                        new Activity
                        {
                            Id = classId + 2,
                            ClassId = classId
                        },
                        new Activity
                        {
                            Id = classId + 1,
                            ClassId = classId
                        }
                    ]));

        var result = await service.GetSemesterSnapshotAsync(
            123,
            10,
            "fake-session");

        Assert.Equal(10, result.SemesterId);
        Assert.Equal([10, 20, 30], result.Classes.Select(item => item.Id));
        Assert.Equal(["First", "Second", "Third"], result.Classes.Select(item => item.Name));
        Assert.Equal([12, 11], result.Classes[0].Activities.Select(activity => activity.Id));
        Assert.Empty(result.Classes[1].Activities);
        Assert.Equal([32, 31], result.Classes[2].Activities.Select(activity => activity.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetActivitiesBySemesterAsync_WithNoClasses_ReturnsEmpty(
        bool repositoryReturnsNull)
    {
        var activityCalls = 0;
        var service = CreateService(
            (_, _, _) => Task.FromResult(
                repositoryReturnsNull
                    ? null
                    : new List<ClassInfo>()),
            (_, _, _, _) =>
            {
                Interlocked.Increment(ref activityCalls);
                return Task.FromResult(new List<Activity>());
            });

        var result = await service.GetActivitiesBySemesterAsync(
            123,
            10,
            "fake-session");

        Assert.Empty(result);
        Assert.Equal(0, activityCalls);
    }

    [Fact]
    public async Task GetActivitiesBySemesterAsync_LimitsActivityRequestsToTwo()
    {
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pairStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activeRequests = 0;
        var maximumRequests = 0;
        var startedRequests = 0;
        var service = CreateService(
            (_, _, _) => Task.FromResult<List<ClassInfo>?>(
            [
                new ClassInfo { Id = 1 },
                new ClassInfo { Id = 2 },
                new ClassInfo { Id = 3 },
                new ClassInfo { Id = 4 }
            ]),
            async (_, classId, _, cancellationToken) =>
            {
                Interlocked.Increment(ref startedRequests);
                var active = Interlocked.Increment(ref activeRequests);
                UpdateMaximum(ref maximumRequests, active);

                if (active == 2)
                {
                    pairStarted.TrySetResult();
                }

                await release.Task.WaitAsync(cancellationToken);
                Interlocked.Decrement(ref activeRequests);

                return [new Activity { ClassId = classId }];
            });

        var request = service.GetActivitiesBySemesterAsync(
            123,
            10,
            "fake-session");

        await pairStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, startedRequests);
        Assert.Equal(2, maximumRequests);

        release.TrySetResult();
        await request;

        Assert.Equal(4, startedRequests);
        Assert.Equal(2, maximumRequests);
    }

    [Fact]
    public async Task GetActivitiesBySemesterAsync_CallerCancellationStopsQueuedWork()
    {
        using var cancellationSource = new CancellationTokenSource();
        var pairStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bothCanceled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startedRequests = 0;
        var canceledRequests = 0;
        var service = CreateService(
            (_, _, _) => Task.FromResult<List<ClassInfo>?>(
            [
                new ClassInfo { Id = 1 },
                new ClassInfo { Id = 2 },
                new ClassInfo { Id = 3 }
            ]),
            async (_, _, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref startedRequests) == 2)
                {
                    pairStarted.TrySetResult();
                }

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    if (Interlocked.Increment(ref canceledRequests) == 2)
                    {
                        bothCanceled.TrySetResult();
                    }

                    throw;
                }

                return [];
            });

        var request = service.GetActivitiesBySemesterAsync(
            123,
            10,
            "fake-session",
            cancellationSource.Token);

        await pairStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await bothCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, startedRequests);
    }

    [Fact]
    public async Task GetActivitiesBySemesterAsync_ClassDiscoveryFailureIsPreserved()
    {
        var expectedException = new SyntheticAggregateException();
        var activityCalls = 0;
        var service = CreateService(
            (_, _, _) => throw expectedException,
            (_, _, _, _) =>
            {
                Interlocked.Increment(ref activityCalls);
                return Task.FromResult(new List<Activity>());
            });

        var actualException = await Assert.ThrowsAsync<SyntheticAggregateException>(() =>
            service.GetActivitiesBySemesterAsync(
                123,
                10,
                "fake-session"));

        Assert.Same(expectedException, actualException);
        Assert.Equal(0, activityCalls);
    }

    [Fact]
    public async Task GetActivitiesBySemesterAsync_ActivityFailureCancelsRemainingClasses()
    {
        var expectedException = new SyntheticAggregateException();
        var pairStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startedRequests = 0;
        var service = CreateService(
            (_, _, _) => Task.FromResult<List<ClassInfo>?>(
            [
                new ClassInfo { Id = 10 },
                new ClassInfo { Id = 11 },
                new ClassInfo { Id = 12 }
            ]),
            async (_, classId, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref startedRequests) == 2)
                {
                    pairStarted.TrySetResult();
                }

                await pairStarted.Task.WaitAsync(cancellationToken);

                if (classId == 10)
                {
                    throw expectedException;
                }

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }

                return [];
            });

        var actualException = await Assert.ThrowsAsync<SyntheticAggregateException>(() =>
            service.GetActivitiesBySemesterAsync(
                123,
                10,
                "fake-session"));

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(expectedException, actualException);
        Assert.Equal(2, startedRequests);
    }

    [Fact]
    public async Task GetActivitiesAsync_RemainsARepositoryPassThrough()
    {
        var expected = new List<Activity>
        {
            new() { Id = 42, ClassId = 10 }
        };
        var service = CreateService(
            (semesterId, token, _) =>
            {
                Assert.Equal(10, semesterId);
                Assert.Equal("fake-session", token);
                return Task.FromResult<List<ClassInfo>?>(
                    [new ClassInfo { Id = 10 }]);
            },
            (userId, classId, token, _) =>
            {
                Assert.Equal(123, userId);
                Assert.Equal(10, classId);
                Assert.Equal("fake-session", token);
                return Task.FromResult(expected);
            });

        var result = await service.GetActivitiesAsync(
            123,
            10,
            10,
            "fake-session");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task GetActivitiesAsync_ClassOutsideSemester_ThrowsNotFoundWithoutActivityCall()
    {
        var activityCalls = 0;
        var service = CreateService(
            (_, _, _) => Task.FromResult<List<ClassInfo>?>(
                [new ClassInfo { Id = 11 }]),
            (_, _, _, _) =>
            {
                Interlocked.Increment(ref activityCalls);
                return Task.FromResult(new List<Activity>());
            });

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetActivitiesAsync(
                123,
                10,
                20,
                "fake-session"));

        Assert.Equal(0, activityCalls);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("structural")]
    [InlineData("transient")]
    public async Task GetActivitiesAsync_ClassDiscoveryFailureIsPreserved(
        string failureKind)
    {
        var activityCalls = 0;
        var service = CreateService(
            (_, _, _) => failureKind switch
            {
                "session" => throw new SessionExpiredException(),
                "structural" => throw new StructuralParseException(
                    "classes.class_cards",
                    "Synthetic structural failure."),
                _ => throw new TransientLeb2Exception(
                    "Synthetic transient failure.")
            },
            (_, _, _, _) =>
            {
                Interlocked.Increment(ref activityCalls);
                return Task.FromResult(new List<Activity>());
            });

        switch (failureKind)
        {
            case "session":
                await Assert.ThrowsAsync<SessionExpiredException>(() =>
                    service.GetActivitiesAsync(123, 10, 20, "fake-session"));
                break;
            case "structural":
                await Assert.ThrowsAsync<StructuralParseException>(() =>
                    service.GetActivitiesAsync(123, 10, 20, "fake-session"));
                break;
            default:
                await Assert.ThrowsAsync<TransientLeb2Exception>(() =>
                    service.GetActivitiesAsync(123, 10, 20, "fake-session"));
                break;
        }

        Assert.Equal(0, activityCalls);
    }

    private static ActivityService CreateService(
        Func<int, string, CancellationToken, Task<List<ClassInfo>?>> classFactory,
        Func<int, int, string, CancellationToken, Task<List<Activity>>> activityFactory)
    {
        var repositoryManager = new StubRepositoryManager(
            new StubScrapingRepository(classFactory),
            new StubActivityRepository(activityFactory));

        return new ActivityService(
            new StubCoreAdapterManager(repositoryManager),
            NullLogger<ActivityService>.Instance);
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

    private sealed class StubCoreAdapterManager : ICoreAdapterManager
    {
        public StubCoreAdapterManager(IRepositoryManager repositoryManager)
        {
            RepositoryManager = repositoryManager;
        }

        public IRepositoryManager RepositoryManager { get; }
    }

    private sealed class StubRepositoryManager : IRepositoryManager
    {
        public StubRepositoryManager(
            IScrapingRepository scrapingRepository,
            IActivityRepository activityRepository)
        {
            ScrapingRepository = scrapingRepository;
            ActivityRepository = activityRepository;
        }

        public IScrapingRepository ScrapingRepository { get; }

        public IActivityRepository ActivityRepository { get; }

        public IUserRepository UserRepository { get; } =
            new UnsupportedUserRepository();

        public IAccessKeyRepository AccessKeyRepository { get; } =
            new UnsupportedAccessKeyRepository();
    }

    private sealed class StubScrapingRepository : IScrapingRepository
    {
        private readonly Func<
            int,
            string,
            CancellationToken,
            Task<List<ClassInfo>?>> _factory;

        public StubScrapingRepository(
            Func<
                int,
                string,
                CancellationToken,
                Task<List<ClassInfo>?>> factory)
        {
            _factory = factory;
        }

        public Task<string?> GetCookieAsync(
            Credentials credentials,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<int>?> GetSemesterIdsAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<ClassInfo>?> GetClassesBySemesterIdAsync(
            int semesterId,
            string token,
            CancellationToken cancellationToken = default)
        {
            return _factory(semesterId, token, cancellationToken);
        }
    }

    private sealed class StubActivityRepository : IActivityRepository
    {
        private readonly Func<
            int,
            int,
            string,
            CancellationToken,
            Task<List<Activity>>> _factory;

        public StubActivityRepository(
            Func<
                int,
                int,
                string,
                CancellationToken,
                Task<List<Activity>>> factory)
        {
            _factory = factory;
        }

        public Task<List<Activity>> GetActivitiesAsync(
            int userId,
            int classId,
            string token,
            CancellationToken cancellationToken = default)
        {
            return _factory(userId, classId, token, cancellationToken);
        }
    }

    private sealed class UnsupportedUserRepository : IUserRepository
    {
        public Task<User?> GetUserByCredentialsAsync(
            Credentials credentials,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class UnsupportedAccessKeyRepository : IAccessKeyRepository
    {
        public Task<AccessKeyState?> GetAccessKeyStateAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpsertUserAndClaimKeyAsync(
            Guid keyId,
            string studentId,
            string name,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SyntheticAggregateException : Exception
    {
    }
}
