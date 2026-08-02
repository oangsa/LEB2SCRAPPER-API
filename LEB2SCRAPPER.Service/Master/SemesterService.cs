using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.Semester;
using LEB2SCRAPPER.Service.Contracts.Master;

namespace LEB2SCRAPPER.Service.Master;

public class SemesterService(ICoreAdapterManager coreAdapterManager) : ISemesterService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;

    public async Task<List<SemesterInfo>?> GetSemestersAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var semesters = await _repositoryManager.ScrapingRepository.GetSemestersAsync(
            token,
            cancellationToken);

        return semesters;
    }
}
