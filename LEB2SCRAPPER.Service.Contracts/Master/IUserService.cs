using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Entity.Models.Authentication;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface IUserService
{
    public Task<User?> GetUserByCredentialsAsync(Credentials credentials);
    public Task<string?> GetCookieAsync(Credentials credentials);
}
