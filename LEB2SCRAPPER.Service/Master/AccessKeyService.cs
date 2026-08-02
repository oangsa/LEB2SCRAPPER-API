using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Service.Contracts.Master;

namespace LEB2SCRAPPER.Service.Master;

public sealed class AccessKeyService(ICoreAdapterManager coreAdapterManager) : IAccessKeyService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;

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
        ValidateKeyId(keyId);

        var normalizedStudentId = NormalizeStudentId(studentId);

        if (leb2UserId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leb2UserId),
                "LEB2 user ID must be greater than zero.");
        }

        return _repositoryManager.AccessKeyRepository.UpsertUserAndClaimKeyAsync(
            keyId,
            normalizedStudentId,
            leb2UserId,
            NormalizeWhitespace(name),
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
}
