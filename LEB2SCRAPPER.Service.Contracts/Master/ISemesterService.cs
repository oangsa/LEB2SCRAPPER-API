using LEB2SCRAPPER.Entity.Models.Semester;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface ISemesterService
{
    Task<List<SemesterInfo>?> GetSemestersAsync(
        string token,
        CancellationToken cancellationToken = default);
}
