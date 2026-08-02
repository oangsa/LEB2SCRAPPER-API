using System.Security.Cryptography;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Service.Contracts.Master;

namespace LEB2SCRAPPER.Service.Master;

public sealed class AccessKeyService(
    ICoreAdapterManager coreAdapterManager,
    DeviceBindingOptions? deviceBindingOptions = null) : IAccessKeyService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;
    private readonly DeviceBindingOptions _deviceBindingOptions =
        deviceBindingOptions ?? new DeviceBindingOptions();

    public async Task<AccessKeyState> ValidateProvisionedKeyAsync(
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        ValidateKeyId(keyId);

        var state = await GetStateAsync(keyId, cancellationToken);
        return state;
    }

    public async Task<AccessKeyState> ValidateActivatedKeyAsync(
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var state = await ValidateProvisionedKeyAsync(keyId, cancellationToken);

        if (!state.IsAssigned)
        {
            throw new AccessKeyNotActivatedException();
        }

        return state;
    }

    public void EnsureStudentIdentity(
        AccessKeyState state,
        string studentId)
    {
        if (state.IsAssigned
            && string.IsNullOrWhiteSpace(state.StudentId))
        {
            throw new AccessKeyReauthenticationRequiredException();
        }

        var normalizedStudentId = NormalizeStudentId(studentId);

        if (state.StudentId is not null
            && !string.Equals(
                NormalizeStudentId(state.StudentId),
                normalizedStudentId,
                StringComparison.Ordinal))
        {
            throw new AccessKeyIdentityMismatchException();
        }
    }

    public void EnsureLeb2UserIdentity(
        AccessKeyState state,
        int leb2UserId)
    {
        if (leb2UserId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leb2UserId),
                "LEB2 user ID must be greater than zero.");
        }

        if (!state.IsAssigned || !state.Leb2UserId.HasValue)
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
        return RegisterSuccessfulLoginCoreAsync(
            keyId,
            studentId,
            leb2UserId,
            name,
            null,
            cancellationToken);
    }

    public Task RegisterSuccessfulLoginWithDeviceAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        DeviceBindingRequest deviceBinding,
        CancellationToken cancellationToken = default)
    {
        return RegisterSuccessfulLoginCoreAsync(
            keyId,
            studentId,
            leb2UserId,
            name,
            deviceBinding,
            cancellationToken);
    }

    private Task RegisterSuccessfulLoginCoreAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        DeviceBindingRequest? deviceBinding,
        CancellationToken cancellationToken)
    {
        ValidateKeyId(keyId);

        var normalizedStudentId = NormalizeStudentId(studentId);

        if (leb2UserId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leb2UserId),
                "LEB2 user ID must be greater than zero.");
        }

        var deviceData = _deviceBindingOptions.Enabled && deviceBinding is not null
            ? CreateDeviceBindingData(deviceBinding)
            : null;

        if (deviceData is null)
        {
            return _repositoryManager.AccessKeyRepository.UpsertUserAndClaimKeyAsync(
                keyId,
                normalizedStudentId,
                leb2UserId,
                NormalizeWhitespace(name),
                cancellationToken);
        }

        return _repositoryManager.AccessKeyRepository.UpsertUserAndClaimKeyWithDeviceAsync(
            keyId,
            normalizedStudentId,
            leb2UserId,
            NormalizeWhitespace(name),
            deviceData,
            cancellationToken);
    }

    public async Task EnsureDeviceBindingAsync(
        AccessKeyState state,
        DeviceBindingRequest? deviceBinding,
        bool allowUnbound,
        CancellationToken cancellationToken = default)
    {
        if (!_deviceBindingOptions.EnforcementEnabled)
        {
            return;
        }

        var deviceData = CreateDeviceBindingData(deviceBinding);

        if (state.DeviceIdHash is null)
        {
            if (allowUnbound)
            {
                return;
            }

            throw new DeviceBindingRequiredException();
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(state.DeviceIdHash),
                Convert.FromHexString(deviceData.DeviceIdHash)))
        {
            throw new DeviceBindingMismatchException();
        }
    }

    public DeviceBindingRequest? PrepareDeviceBindingForLogin(
        AccessKeyState state,
        DeviceBindingRequest? deviceBinding)
    {
        if (!_deviceBindingOptions.Enabled || deviceBinding is null)
        {
            return null;
        }

        var deviceData = CreateDeviceBindingData(deviceBinding);

        if (state.DeviceIdHash is null
            || _deviceBindingOptions.EnforcementEnabled
            || CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(state.DeviceIdHash),
                Convert.FromHexString(deviceData.DeviceIdHash)))
        {
            return deviceBinding;
        }

        return null;
    }

    public async Task LogoutAsync(
        AccessKeyState state,
        DeviceBindingRequest? deviceBinding,
        CancellationToken cancellationToken = default)
    {
        if (!_deviceBindingOptions.Enabled || deviceBinding is null)
        {
            return;
        }

        var deviceData = CreateDeviceBindingData(deviceBinding);
        await _repositoryManager.AccessKeyRepository.UnbindDeviceAsync(
            state.KeyId,
            deviceData.DeviceIdHash,
            "client-logout",
            cancellationToken);
    }

    private async Task<AccessKeyState> GetStateAsync(
        Guid keyId,
        CancellationToken cancellationToken)
    {
        var state = await _repositoryManager.AccessKeyRepository.GetAccessKeyStateAsync(
            keyId,
            cancellationToken);

        if (state is null)
        {
            throw new AccessKeyInvalidException();
        }

        return state;
    }

    private static void ValidateKeyId(Guid keyId)
    {
        if (keyId == Guid.Empty)
        {
            throw new AccessKeyInvalidException();
        }
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeStudentId(string value)
    {
        var normalizedStudentId = value.Trim();

        if (normalizedStudentId.Length == 0)
        {
            throw new ArgumentException(
                "Student identifier cannot be empty.",
                nameof(value));
        }

        return normalizedStudentId;
    }

    private DeviceBindingData CreateDeviceBindingData(
        DeviceBindingRequest? deviceBinding)
    {
        if (deviceBinding is null
            || string.IsNullOrWhiteSpace(deviceBinding.DeviceId))
        {
            throw new DeviceIdRequiredException();
        }

        var normalizedDeviceId = NormalizeHeaderValue(deviceBinding.DeviceId);

        if (normalizedDeviceId.Length > 512)
        {
            throw new DeviceIdInvalidException();
        }

        return new DeviceBindingData(
            DeviceIdHasher.Hash(
                _deviceBindingOptions.HmacSecret,
                normalizedDeviceId),
            NormalizeOptionalHeaderValue(deviceBinding.DeviceName),
            NormalizeOptionalHeaderValue(deviceBinding.Platform),
            NormalizeOptionalHeaderValue(deviceBinding.OsVersion),
            NormalizeOptionalHeaderValue(deviceBinding.AppVersion));
    }

    private static string NormalizeHeaderValue(string value)
    {
        return value.Trim();
    }

    private static string? NormalizeOptionalHeaderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > 256)
        {
            throw new DeviceIdInvalidException();
        }

        return normalized;
    }
}
