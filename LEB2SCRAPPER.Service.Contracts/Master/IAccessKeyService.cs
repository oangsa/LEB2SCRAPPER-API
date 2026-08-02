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
}
