using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Entity.Models.Authentication;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IUserRepository
{
    Task<User?> GetUserByCredentialsAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default);
}
