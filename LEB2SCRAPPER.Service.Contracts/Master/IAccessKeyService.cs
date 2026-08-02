using LEB2SCRAPPER.Entity.Models.AccessKey;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface IAccessKeyService
{
    Task<AccessKeyState> ValidateProvisionedKeyAsync(
        Guid keyId,
        CancellationToken cancellationToken = default);

    Task<AccessKeyState> ValidateActivatedKeyAsync(
        Guid keyId,
        CancellationToken cancellationToken = default);

    void EnsureStudentIdentity(
        AccessKeyState state,
        string studentId);

    void EnsureLeb2IdentityInitialized(AccessKeyState state);

    void EnsureLeb2UserIdentity(
        AccessKeyState state,
        int leb2UserId);

    Task RegisterSuccessfulLoginAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        CancellationToken cancellationToken = default);

    Task RegisterSuccessfulLoginWithDeviceAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        DeviceBindingRequest deviceBinding,
        CancellationToken cancellationToken = default)
    {
        return RegisterSuccessfulLoginAsync(
            keyId,
            studentId,
            leb2UserId,
            name,
            cancellationToken);
    }

    Task EnsureDeviceBindingAsync(
        AccessKeyState state,
        DeviceBindingRequest? deviceBinding,
        bool allowUnbound,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    DeviceBindingRequest? PrepareDeviceBindingForLogin(
        AccessKeyState state,
        DeviceBindingRequest? deviceBinding)
    {
        return deviceBinding;
    }

    Task LogoutAsync(
        AccessKeyState state,
        DeviceBindingRequest? deviceBinding,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
