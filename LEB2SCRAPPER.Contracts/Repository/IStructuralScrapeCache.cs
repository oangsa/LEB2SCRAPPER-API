using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Semester;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IStructuralScrapeCache
{
    Task<List<SemesterInfo>?> GetSemestersAsync(
        string clientKey,
        Func<CancellationToken, Task<List<SemesterInfo>?>> valueFactory,
        CancellationToken cancellationToken = default);

    Task<List<ClassInfo>?> GetClassesAsync(
        string clientKey,
        int semesterId,
        Func<CancellationToken, Task<List<ClassInfo>?>> valueFactory,
        CancellationToken cancellationToken = default);
}
