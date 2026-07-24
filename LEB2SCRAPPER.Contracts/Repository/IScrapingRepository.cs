using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IScrapingRepository
{
    Task<string?> GetCookieAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default);

    Task<List<int>?> GetSemesterIdsAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<List<ClassInfo>?> GetClassesBySemesterIdAsync(
        int semesterId,
        string token,
        CancellationToken cancellationToken = default);
}
