using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Service.Contracts.Master;

namespace LEB2SCRAPPER.Service.Master;

public class SemesterService(ICoreAdapterManager coreAdapterManager) : ISemesterService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;

    public async Task<List<int>?> GetSemestersAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var semesters = await _repositoryManager.ScrapingRepository.GetSemesterIdsAsync(
            token,
            cancellationToken);

        return semesters;
    }
}
