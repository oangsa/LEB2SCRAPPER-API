namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface ISemesterService
{
    public Task<List<int>?> GetSemestersAsync(string token);
}
