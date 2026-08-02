using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Entity.Models.Authentication;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface IUserService
{
    Task<User?> GetUserByCredentialsAsync(
        Credentials credentials,
        AccessKeyState accessKeyState,
        CancellationToken cancellationToken = default);

    Task<string?> GetCookieAsync(
        Credentials credentials,
        AccessKeyState accessKeyState,
        CancellationToken cancellationToken = default);
}
