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
using LEB2SCRAPPER.Service.Master;

namespace LEB2SCRAPPER.Tests.Service;

public class AccessKeyServiceTests
{
    private static readonly Guid KeyId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task ValidateProvisionedKey_AllowsUnassignedKey()
    {
        var repository = new StubAccessKeyRepository
        {
            State = new AccessKeyState(KeyId, null, null, null)
        };
        var service = CreateService(repository);

        var state = await service.ValidateProvisionedKeyAsync(KeyId);

        Assert.Equal(KeyId, state.KeyId);
        Assert.False(state.IsAssigned);
        Assert.Null(state.Leb2UserId);
    }

    [Fact]
    public async Task ValidateProvisionedKey_RejectsUnknownKey()
    {
        var repository = new StubAccessKeyRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<AccessKeyInvalidException>(() =>
            service.ValidateProvisionedKeyAsync(
                KeyId));
    }

    [Fact]
    public async Task ValidateActivatedKey_RejectsUnassignedKey()
    {
        var repository = new StubAccessKeyRepository
        {
            State = new AccessKeyState(KeyId, null, null, null)
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<AccessKeyNotActivatedException>(() =>
            service.ValidateActivatedKeyAsync(KeyId));
    }

    [Fact]
    public async Task RegisterSuccessfulLogin_NormalizesStudentAndNameBeforePersistence()
    {
        var repository = new StubAccessKeyRepository();
        var service = CreateService(repository);

        await service.RegisterSuccessfulLoginAsync(
            KeyId,
            "  student-001  ",
            1001,
            "  Example   Student ");

        Assert.Equal(KeyId, repository.RegisteredKeyId);
        Assert.Equal("student-001", repository.RegisteredStudentId);
        Assert.Equal(1001, repository.RegisteredLeb2UserId);
        Assert.Equal("Example Student", repository.RegisteredName);
    }

    [Fact]
    public async Task DeviceBinding_UsesHmacAndRejectsMismatch()
    {
        var repository = new StubAccessKeyRepository();
        var service = CreateService(
            repository,
            new DeviceBindingOptions
            {
                Enabled = true,
                EnforcementEnabled = true,
                HmacSecret = "test-secret"
            });
        var request = new DeviceBindingRequest(
            "device-one",
            "Test phone",
            "android",
            "14",
            "0.5.0");

        await service.RegisterSuccessfulLoginWithDeviceAsync(
            KeyId,
            "student-001",
            1001,
            "Example Student",
            request);

        Assert.NotNull(repository.RegisteredDevice);
        Assert.NotEqual("device-one", repository.RegisteredDevice?.DeviceIdHash);

        var state = new AccessKeyState(
            KeyId,
            UserId,
            "student-001",
            1001,
            repository.RegisteredDevice?.DeviceIdHash);
        await service.EnsureDeviceBindingAsync(state, request, false);

        await Assert.ThrowsAsync<DeviceBindingMismatchException>(() =>
            service.EnsureDeviceBindingAsync(
                state,
                request with { DeviceId = "device-two" },
                false));
    }

    [Fact]
    public async Task Logout_UnbindsDeviceWithoutChangingAccountOwnership()
    {
        var repository = new StubAccessKeyRepository();
        var service = CreateService(
            repository,
            new DeviceBindingOptions
            {
                Enabled = true,
                HmacSecret = "test-secret"
            });

        await service.LogoutAsync(
            new AccessKeyState(KeyId, UserId, "student-001", 1001),
            new DeviceBindingRequest("device-one", null, null, null, null));

        Assert.NotNull(repository.UnboundDeviceHash);
        Assert.Equal(KeyId, repository.UnboundKeyId);
    }

    [Fact]
    public void EnsureStudentIdentity_AllowsNormalizedMatchingIdentity()
    {
        var service = CreateService(new StubAccessKeyRepository());

        service.EnsureStudentIdentity(
            new AccessKeyState(KeyId, UserId, "student-001", 1001),
            " student-001 ");
    }

    [Fact]
    public void EnsureStudentIdentity_AllowsUnassignedState()
    {
        var service = CreateService(new StubAccessKeyRepository());

        service.EnsureStudentIdentity(
            new AccessKeyState(KeyId, null, null, null),
            "student-001");
    }

    [Fact]
    public void EnsureStudentIdentity_RejectsAssignedStateWithoutStudent()
    {
        var service = CreateService(new StubAccessKeyRepository());

        Assert.Throws<AccessKeyReauthenticationRequiredException>(() =>
            service.EnsureStudentIdentity(
                new AccessKeyState(KeyId, UserId, null, 1001),
                "student-001"));
    }

    [Fact]
    public void EnsureStudentIdentity_RejectsDifferentIdentity()
    {
        var service = CreateService(new StubAccessKeyRepository());

        Assert.Throws<AccessKeyIdentityMismatchException>(() =>
            service.EnsureStudentIdentity(
                new AccessKeyState(KeyId, UserId, "student-001", 1001),
                "student-002"));
    }

    [Fact]
    public void EnsureLeb2UserIdentity_RejectsLegacyNullIdentity()
    {
        var service = CreateService(new StubAccessKeyRepository());

        Assert.Throws<AccessKeyReauthenticationRequiredException>(() =>
            service.EnsureLeb2UserIdentity(
                new AccessKeyState(KeyId, UserId, "student-001", null),
                1001));
    }

    [Fact]
    public void EnsureLeb2IdentityInitialized_RejectsLegacyNullIdentity()
    {
        var service = CreateService(new StubAccessKeyRepository());

        Assert.Throws<AccessKeyReauthenticationRequiredException>(() =>
            service.EnsureLeb2IdentityInitialized(
                new AccessKeyState(KeyId, UserId, "student-001", null)));
    }

    [Fact]
    public void EnsureLeb2UserIdentity_RejectsUnassignedState()
    {
        var service = CreateService(new StubAccessKeyRepository());

        Assert.Throws<AccessKeyReauthenticationRequiredException>(() =>
            service.EnsureLeb2UserIdentity(
                new AccessKeyState(KeyId, null, null, 1001),
                1001));
    }

    [Fact]
    public void EnsureLeb2UserIdentity_RejectsDifferentIdentity()
    {
        var service = CreateService(new StubAccessKeyRepository());

        Assert.Throws<AccessKeyIdentityMismatchException>(() =>
            service.EnsureLeb2UserIdentity(
                new AccessKeyState(KeyId, UserId, "student-001", 1001),
                1002));
    }

    private static AccessKeyService CreateService(
        StubAccessKeyRepository repository,
        DeviceBindingOptions? deviceBindingOptions = null)
    {
        return new AccessKeyService(
            new StubCoreAdapterManager(
                new StubRepositoryManager(repository)),
            deviceBindingOptions);
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

        public int? RegisteredLeb2UserId { get; private set; }

        public string? RegisteredName { get; private set; }

        public DeviceBindingData? RegisteredDevice { get; private set; }

        public Guid? UnboundKeyId { get; private set; }

        public string? UnboundDeviceHash { get; private set; }

        public Task<AccessKeyState?> GetAccessKeyStateAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(State);
        }

        public Task UpsertUserAndClaimKeyAsync(
            Guid keyId,
            string studentId,
            int leb2UserId,
            string name,
            CancellationToken cancellationToken = default)
        {
            RegisteredKeyId = keyId;
            RegisteredStudentId = studentId;
            RegisteredLeb2UserId = leb2UserId;
            RegisteredName = name;
            return Task.CompletedTask;
        }

        public Task UpsertUserAndClaimKeyWithDeviceAsync(
            Guid keyId,
            string studentId,
            int leb2UserId,
            string name,
            DeviceBindingData deviceBinding,
            CancellationToken cancellationToken = default)
        {
            RegisteredKeyId = keyId;
            RegisteredStudentId = studentId;
            RegisteredLeb2UserId = leb2UserId;
            RegisteredName = name;
            RegisteredDevice = deviceBinding;
            return Task.CompletedTask;
        }

        public Task UnbindDeviceAsync(
            Guid keyId,
            string deviceIdHash,
            string reason,
            CancellationToken cancellationToken = default)
        {
            UnboundKeyId = keyId;
            UnboundDeviceHash = deviceIdHash;
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
