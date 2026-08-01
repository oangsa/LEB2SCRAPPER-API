using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Service.Contracts.Master;


namespace LEB2SCRAPPER.Service.Master;

public class UserService(
    ICoreAdapterManager coreAdapterManager,
    IAccessKeyService accessKeyService) : IUserService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;

    public async Task<string?> GetCookieAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default)
    {
        var cookie = await _repositoryManager.ScrapingRepository.GetCookieAsync(
            credentials,
            cancellationToken);

        return cookie;
    }

    public async Task<User?> GetUserByCredentialsAsync(
        Credentials credentials,
        Guid accessKeyId,
        CancellationToken cancellationToken = default)
    {
        var user = await _repositoryManager.UserRepository.GetUserByCredentialsAsync(
            credentials,
            cancellationToken);

        if (user is not null)
        {
            var name = string.Join(
                ' ',
                new[]
                {
                    user.NameEnglish,
                    user.SurnameEnglish
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.Join(
                    ' ',
                    new[]
                    {
                        user.NameThai,
                        user.SurnameThai
                    }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            await accessKeyService.RegisterSuccessfulLoginAsync(
                accessKeyId,
                credentials.Username.Trim(),
                name,
                cancellationToken);
        }

        return user;
    }
}
