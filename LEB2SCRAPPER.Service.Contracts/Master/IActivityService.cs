using LEB2SCRAPPER.Entity.Models.Activity;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface IActivityService
{
    public Task<List<Activity>?> GetActivitiesAsync(int userId, int classId, string token);
}
