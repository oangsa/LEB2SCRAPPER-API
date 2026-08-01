using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Service;
using LEB2SCRAPPER.Service.Master;

namespace LEB2SCRAPPER.Tests.Service;

public class AccessKeyServiceTests
{
    [Fact]
    public async Task ValidateProvisionedKey_AllowsUnassignedKey()
    {
        var keyId = Guid.Parse("9a7b979b-a361-4170-aee7-cba89445495b");
        var repository = new StubAccessKeyRepository
        {
            State = new AccessKeyState(keyId, null, null)
        };
        var service = CreateService(repository);

        var state = await service.ValidateProvisionedKeyAsync(keyId);

        Assert.Equal(keyId, state.KeyId);
        Assert.False(state.IsAssigned);
    }

    [Fact]
    public async Task ValidateProvisionedKey_RejectsUnknownKey()
    {
        var repository = new StubAccessKeyRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<AccessKeyInvalidException>(() =>
            service.ValidateProvisionedKeyAsync(
                Guid.Parse("9a7b979b-a361-4170-aee7-cba89445495b")));
    }

    [Fact]
    public async Task ValidateActivatedKey_RejectsUnassignedKey()
    {
        var keyId = Guid.Parse("9a7b979b-a361-4170-aee7-cba89445495b");
        var repository = new StubAccessKeyRepository
        {
            State = new AccessKeyState(keyId, null, null)
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<AccessKeyNotActivatedException>(() =>
            service.ValidateActivatedKeyAsync(keyId));
    }

    [Fact]
    public async Task RegisterSuccessfulLogin_NormalizesStudentAndNameBeforePersistence()
    {
        var repository = new StubAccessKeyRepository();
        var service = CreateService(repository);
        var keyId = Guid.Parse("9a7b979b-a361-4170-aee7-cba89445495b");

        await service.RegisterSuccessfulLoginAsync(
            keyId,
            "  60000000  ",
            "  Example   Student ");

        Assert.Equal(keyId, repository.RegisteredKeyId);
        Assert.Equal("60000000", repository.RegisteredStudentId);
        Assert.Equal("Example Student", repository.RegisteredName);
    }

    private static AccessKeyService CreateService(StubAccessKeyRepository repository)
    {
        return new AccessKeyService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(repository)));
    }

    private sealed class StubCoreAdapterManager : ICoreAdapterManager
    {
        public StubCoreAdapterManager(IRepositoryManager repositoryManager)
        {
            RepositoryManager = repositoryManager;
        }

        public IRepositoryManager RepositoryManager { get; }
    }

    private sealed class StubAccessKeyRepository : IAccessKeyRepository
    {
        public AccessKeyState? State { get; init; }

        public Guid? RegisteredKeyId { get; private set; }

        public string? RegisteredStudentId { get; private set; }

        public string? RegisteredName { get; private set; }

        public Task<AccessKeyState?> GetAccessKeyStateAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(State);
        }

        public Task UpsertUserAndClaimKeyAsync(
            Guid keyId,
            string studentId,
            string name,
            CancellationToken cancellationToken = default)
        {
            RegisteredKeyId = keyId;
            RegisteredStudentId = studentId;
            RegisteredName = name;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRepositoryManager : IRepositoryManager
    {
        public StubRepositoryManager(IAccessKeyRepository accessKeyRepository)
        {
            AccessKeyRepository = accessKeyRepository;
        }

        public IScrapingRepository ScrapingRepository { get; } =
            new UnsupportedScrapingRepository();

        public IActivityRepository ActivityRepository { get; } =
            new UnsupportedActivityRepository();

        public IUserRepository UserRepository { get; } =
            new UnsupportedUserRepository();

        public IAccessKeyRepository AccessKeyRepository { get; }
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

    private sealed class UnsupportedUserRepository : IUserRepository
    {
        public Task<User?> GetUserByCredentialsAsync(
            Credentials credentials,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
