using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.Response;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface IActivityService
{
    Task<List<Activity>?> GetActivitiesAsync(
        int userId,
        int classId,
        string token,
        CancellationToken cancellationToken = default);

    Task<List<Activity>> GetActivitiesBySemesterAsync(
        int userId,
        int semesterId,
        string token,
        CancellationToken cancellationToken = default);

    Task<SemesterSnapshotResponse> GetSemesterSnapshotAsync(
        int userId,
        int semesterId,
        string token,
        CancellationToken cancellationToken = default);
}
