using LEB2SCRAPPER.Service.Contracts.Master;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.Activity;

namespace LEB2SCRAPPER.Service.Master;

public class ActivityService(ICoreAdapterManager coreAdapterManager) : IActivityService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;

    public async Task<List<Activity>?> GetActivitiesAsync(int userId, int classId, string token)
    {
        var activities = await _repositoryManager.ActivityRepository.GetActivitiesAsync(userId, classId, token);
        return activities;
    }
}
