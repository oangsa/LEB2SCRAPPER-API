using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Entity.Models.Authentication;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface IUserService
{
    Task<User?> GetUserByCredentialsAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default);

    Task<string?> GetCookieAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default);
}
