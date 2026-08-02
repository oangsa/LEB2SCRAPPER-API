using LEB2SCRAPPER.Entity.Models.AccessKey;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IAccessKeyRepository
{
    Task<AccessKeyState?> GetAccessKeyStateAsync(
        Guid keyId,
        CancellationToken cancellationToken = default);

    Task UpsertUserAndClaimKeyAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        CancellationToken cancellationToken = default);

    Task UpsertUserAndClaimKeyWithDeviceAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        DeviceBindingData deviceBinding,
        CancellationToken cancellationToken = default)
    {
        return UpsertUserAndClaimKeyAsync(
            keyId,
            studentId,
            leb2UserId,
            name,
            cancellationToken);
    }

    Task UnbindDeviceAsync(
        Guid keyId,
        string deviceIdHash,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
