using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Semester;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IScrapingRepository
{
    Task<string?> GetCookieAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default);

    Task<List<SemesterInfo>?> GetSemestersAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<List<ClassInfo>?> GetClassesBySemesterIdAsync(
        int semesterId,
        string token,
        CancellationToken cancellationToken = default);
}
