using LEB2SCRAPPER.Entity.Models.Activity;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IActivityResultCache
{
    Task<List<Activity>> GetActivitiesAsync(
        string clientKey,
        int userId,
        int classId,
        Func<CancellationToken, Task<List<Activity>>> valueFactory,
        CancellationToken cancellationToken = default);
}
