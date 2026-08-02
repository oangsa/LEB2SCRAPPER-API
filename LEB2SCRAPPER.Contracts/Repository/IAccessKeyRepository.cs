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
}
