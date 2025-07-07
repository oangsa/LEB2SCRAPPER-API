using LEB2SCRAPPER.Entity.Models.Activity;

namespace LEB2SCRAPPER.Contracts.Repository;

public interface IActivityRepository
{
    Task<List<Activity>> GetActivitiesAsync(int userId, int classId, string token);
}
