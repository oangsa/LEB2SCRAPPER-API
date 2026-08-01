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

    Task RegisterSuccessfulLoginAsync(
        Guid keyId,
        string studentId,
        string name,
        CancellationToken cancellationToken = default);
}
