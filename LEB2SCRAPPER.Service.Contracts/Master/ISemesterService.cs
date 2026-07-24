namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface ISemesterService
{
    Task<List<int>?> GetSemestersAsync(
        string token,
        CancellationToken cancellationToken = default);
}
