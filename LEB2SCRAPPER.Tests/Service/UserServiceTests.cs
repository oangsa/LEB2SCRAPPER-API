using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Semester;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Service;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Service.Contracts.Master;
using LEB2SCRAPPER.Service.Core;
using LEB2SCRAPPER.Service.Master;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LEB2SCRAPPER.Tests.Service;

public class UserServiceTests
{
    private static readonly Guid KeyId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task SuccessfulLogin_RegistersLocalUserAfterLeb2Login()
    {
        var accessKeyService = new StubAccessKeyService();
        var service = new UserService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(
                    new UserRepositoryStub
                    {
                        User = new User
                        {
                            Id = 42,
                            KmuttId = "student-001",
                            NameEnglish = "Example",
                            SurnameEnglish = "Student"
                        }
                    })),
            accessKeyService);

        var user = await service.GetUserByCredentialsAsync(
            new Credentials
            {
                Username = "  student-001 ",
                Password = "fake-password"
            },
            new AccessKeyState(KeyId, null, null, null));

        Assert.NotNull(user);
        Assert.Equal(KeyId, accessKeyService.KeyId);
        Assert.Equal("student-001", accessKeyService.StudentId);
        Assert.Equal(42, accessKeyService.Leb2UserId);
        Assert.Equal("Example Student", accessKeyService.Name);
    }

    [Fact]
    public async Task FailedLeb2Login_DoesNotRegisterLocalUser()
    {
        var accessKeyService = new StubAccessKeyService();
        var service = new UserService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(
                    new UserRepositoryStub
                    {
                        Exception = new InvalidOperationException("fake LEB2 failure")
                    })),
            accessKeyService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetUserByCredentialsAsync(
                new Credentials
                {
                    Username = "student-001",
                    Password = "fake-password"
                },
                new AccessKeyState(KeyId, null, null, null)));

        Assert.Null(accessKeyService.KeyId);
    }

    [Fact]
    public async Task LoginWithDifferentAuthoritativeLeb2Identity_ReturnsIdentityConflict()
    {
        var accessKeyService = new StubAccessKeyService
        {
            RegisterException = new AccessKeyIdentityConflictException()
        };
        var service = new UserService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(
                    new UserRepositoryStub
                    {
                        User = new User
                        {
                            Id = 1002,
                            KmuttId = "student-001"
                        }
                    })),
            accessKeyService);

        await Assert.ThrowsAsync<AccessKeyIdentityConflictException>(() =>
            service.GetUserByCredentialsAsync(
                new Credentials
                {
                    Username = "student-001",
                    Password = "fake-password"
                },
                new AccessKeyState(
                    KeyId,
                    Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    "student-001",
                    1001)));
    }

    [Fact]
    public async Task LoginIdentityMismatch_StopsBeforeLeb2Repository()
    {
        var service = new UserService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(
                    new UserRepositoryStub
                    {
                        Exception = new InvalidOperationException(
                            "LEB2 repository should not run.")
                    })),
            new StubAccessKeyService());

        await Assert.ThrowsAsync<AccessKeyIdentityMismatchException>(() =>
            service.GetUserByCredentialsAsync(
                new Credentials
                {
                    Username = "student-002",
                    Password = "fake-password"
                },
                new AccessKeyState(
                    KeyId,
                    Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    "student-001",
                    1001)));
    }

    [Fact]
    public async Task CookieIdentityMismatch_StopsBeforeScrapingRepository()
    {
        var service = new UserService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(new UserRepositoryStub())),
            new StubAccessKeyService());

        await Assert.ThrowsAsync<AccessKeyIdentityMismatchException>(() =>
            service.GetCookieAsync(
                new Credentials
                {
                    Username = "student-002",
                    Password = "fake-password"
                },
                new AccessKeyState(
                    KeyId,
                    Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    "student-001",
                1001)));
    }

    [Fact]
    public async Task CookieLegacyNullLeb2Identity_StopsBeforeScrapingRepository()
    {
        var service = new UserService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(new UserRepositoryStub())),
            new StubAccessKeyService());

        await Assert.ThrowsAsync<AccessKeyReauthenticationRequiredException>(() =>
            service.GetCookieAsync(
                new Credentials
                {
                    Username = "student-001",
                    Password = "fake-password"
                },
                new AccessKeyState(
                    KeyId,
                    Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    "student-001",
                    null)));
    }

    [Fact]
    public async Task ServiceManagerLogin_RegistersDeviceBindingFromRequestContext()
    {
        var accessKeyService = new StubAccessKeyService();
        var services = new ServiceCollection()
            .AddScoped<ICoreAdapterManager>(_ => new StubCoreAdapterManager(
                new StubRepositoryManager(
                    new UserRepositoryStub
                    {
                        User = new User
                        {
                            Id = 42,
                            KmuttId = "student-001",
                            NameEnglish = "Example",
                            SurnameEnglish = "Student"
                        }
                    })))
            .AddSingleton<ILogger<ActivityService>>(NullLogger<ActivityService>.Instance)
            .AddSingleton<IAccessKeyService>(accessKeyService)
            .AddScoped<DeviceBindingRequestContext>()
            .AddScoped<IServiceManager, ServiceManager>()
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        scope.ServiceProvider
            .GetRequiredService<DeviceBindingRequestContext>()
            .Set(new DeviceBindingRequest(
                "device-1",
                "Pixel",
                "android",
                "15",
                "0.5.0"));

        await scope.ServiceProvider
            .GetRequiredService<IServiceManager>()
            .UserService
            .GetUserByCredentialsAsync(
                new Credentials
                {
                    Username = "student-001",
                    Password = "fake-password"
                },
                new AccessKeyState(KeyId, null, null, null));

        Assert.Equal("device-1", accessKeyService.DeviceId);
    }

    private sealed class StubAccessKeyService : IAccessKeyService
    {
        public string? DeviceId { get; private set; }

        public Guid? KeyId { get; private set; }

        public string? StudentId { get; private set; }

        public int? Leb2UserId { get; private set; }

        public string? Name { get; private set; }

        public Exception? RegisterException { get; init; }

        public Task<AccessKeyState> ValidateProvisionedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AccessKeyState(keyId, null, null, null));
        }

        public Task<AccessKeyState> ValidateActivatedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AccessKeyState(
                keyId,
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                "student-001",
                1001));
        }

        public void EnsureStudentIdentity(
            AccessKeyState state,
            string studentId)
        {
            if (state.StudentId is not null
                && !string.Equals(
                    state.StudentId,
                    studentId.Trim(),
                    StringComparison.Ordinal))
            {
                throw new AccessKeyIdentityMismatchException();
            }
        }

        public void EnsureLeb2UserIdentity(
            AccessKeyState state,
            int leb2UserId)
        {
            if (!state.Leb2UserId.HasValue)
            {
                throw new AccessKeyReauthenticationRequiredException();
            }

            if (state.Leb2UserId.Value != leb2UserId)
            {
                throw new AccessKeyIdentityMismatchException();
            }
        }

        public void EnsureLeb2IdentityInitialized(AccessKeyState state)
        {
            if (!state.IsAssigned || !state.Leb2UserId.HasValue)
            {
                throw new AccessKeyReauthenticationRequiredException();
            }
        }

        public Task RegisterSuccessfulLoginAsync(
            Guid keyId,
            string studentId,
            int leb2UserId,
            string name,
            CancellationToken cancellationToken = default)
        {
            if (RegisterException is not null)
            {
                throw RegisterException;
            }

            KeyId = keyId;
            StudentId = studentId;
            Leb2UserId = leb2UserId;
            Name = name;
            return Task.CompletedTask;
        }

        public Task RegisterSuccessfulLoginWithDeviceAsync(
            Guid keyId,
            string studentId,
            int leb2UserId,
            string name,
            DeviceBindingRequest deviceBinding,
            CancellationToken cancellationToken = default)
        {
            DeviceId = deviceBinding.DeviceId;
            return RegisterSuccessfulLoginAsync(
                keyId,
                studentId,
                leb2UserId,
                name,
                cancellationToken);
        }
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
        public StubRepositoryManager(IUserRepository userRepository)
        {
            UserRepository = userRepository;
        }

        public IScrapingRepository ScrapingRepository { get; } =
            new UnsupportedScrapingRepository();

        public IActivityRepository ActivityRepository { get; } =
            new UnsupportedActivityRepository();

        public IUserRepository UserRepository { get; }

        public IAccessKeyRepository AccessKeyRepository { get; } =
            new UnsupportedAccessKeyRepository();
    }

    private sealed class UserRepositoryStub : IUserRepository
    {
        public User? User { get; init; }

        public Exception? Exception { get; init; }

        public Task<User?> GetUserByCredentialsAsync(
            Credentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(User);
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
            int leb2UserId,
            string name,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class UnsupportedActivityRepository : IActivityRepository
    {
        public Task<List<LEB2SCRAPPER.Entity.Models.Activity.Activity>> GetActivitiesAsync(
            int userId,
            int classId,
            string token,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class UnsupportedScrapingRepository : IScrapingRepository
    {
        public Task<string?> GetCookieAsync(
            Credentials credentials,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<SemesterInfo>?> GetSemestersAsync(
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
            throw new NotSupportedException();
        }
    }
}
