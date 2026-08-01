using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Service;
using LEB2SCRAPPER.Service.Contracts.Master;
using LEB2SCRAPPER.Service.Master;

namespace LEB2SCRAPPER.Tests.Service;

public class UserServiceTests
{
    [Fact]
    public async Task SuccessfulLogin_RegistersLocalUserAfterLeb2Login()
    {
        var keyId = Guid.Parse("9a7b979b-a361-4170-aee7-cba89445495b");
        var accessKeyService = new StubAccessKeyService();
        var service = new UserService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(
                    new UserRepositoryStub
                    {
                        User = new User
                        {
                            Id = 42,
                            KmuttId = "60000000",
                            NameEnglish = "Example",
                            SurnameEnglish = "Student"
                        }
                    })),
            accessKeyService);

        var user = await service.GetUserByCredentialsAsync(
            new Credentials
            {
                Username = " 60000000 ",
                Password = "fake-password"
            },
            keyId);

        Assert.NotNull(user);
        Assert.Equal(keyId, accessKeyService.KeyId);
        Assert.Equal("60000000", accessKeyService.StudentId);
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
                    Username = "60000000",
                    Password = "fake-password"
                },
                Guid.Parse("9a7b979b-a361-4170-aee7-cba89445495b")));

        Assert.Null(accessKeyService.KeyId);
    }

    private sealed class StubAccessKeyService : IAccessKeyService
    {
        public Guid? KeyId { get; private set; }

        public string? StudentId { get; private set; }

        public string? Name { get; private set; }

        public Task<AccessKeyState> ValidateProvisionedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AccessKeyState(keyId, null, null));
        }

        public Task<AccessKeyState> ValidateActivatedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AccessKeyState(
                keyId,
                Guid.NewGuid(),
                "60000000"));
        }

        public Task RegisterSuccessfulLoginAsync(
            Guid keyId,
            string studentId,
            string name,
            CancellationToken cancellationToken = default)
        {
            KeyId = keyId;
            StudentId = studentId;
            Name = name;
            return Task.CompletedTask;
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
            throw new NotSupportedException();
        }
    }
}
