using LEB2SCRAPPER.Entity.Models.Class;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IStructuralScrapeCache
{
    Task<List<int>?> GetSemesterIdsAsync(
        string clientKey,
        Func<CancellationToken, Task<List<int>?>> valueFactory,
        CancellationToken cancellationToken = default);

    Task<List<ClassInfo>?> GetClassesAsync(
        string clientKey,
        int semesterId,
        Func<CancellationToken, Task<List<ClassInfo>?>> valueFactory,
        CancellationToken cancellationToken = default);
}
