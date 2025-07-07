using LEB2SCRAPPER.Service.Contracts.Master;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.Class;


namespace LEB2SCRAPPER.Service.Master;

public class ClassService(ICoreAdapterManager coreAdapterManager) : IClassService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;

    public async Task<List<ClassInfo>?> GetClassesAsync(int semesterId, string token)
    {
        var classes = await _repositoryManager.ScrapingRepository.GetClassesBySemesterIdAsync(semesterId, token);
        return classes;
    }
}
