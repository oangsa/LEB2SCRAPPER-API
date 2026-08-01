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

    public Task RegisterSuccessfulLoginAsync(
        Guid keyId,
        string studentId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateKeyId(keyId);

        var normalizedStudentId = studentId.Trim();

        if (normalizedStudentId.Length == 0)
        {
            throw new ArgumentException(
                "Student identifier cannot be empty.",
                nameof(studentId));
        }

        return _repositoryManager.AccessKeyRepository.UpsertUserAndClaimKeyAsync(
            keyId,
            normalizedStudentId,
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
}
