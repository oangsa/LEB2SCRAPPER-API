using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Service.Contracts.Master;


namespace LEB2SCRAPPER.Service.Master;

public class UserService(ICoreAdapterManager coreAdapterManager) : IUserService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;

    public async Task<string?> GetCookieAsync(Credentials credentials)
    {
        var cookie = await _repositoryManager.ScrapingRepository.GetCookieAsync(credentials);
        return cookie;
    }

    public async Task<User?> GetUserByCredentialsAsync(Credentials credentials)
    {
        var user = await _repositoryManager.UserRepository.GetUserByCredentialsAsync(credentials);
        return user;
    }
}
