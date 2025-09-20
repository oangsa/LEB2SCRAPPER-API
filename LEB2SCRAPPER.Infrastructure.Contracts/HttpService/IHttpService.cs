namespace LEB2SCRAPPER.Infrastructure.Contracts.HttpService;

public interface IHttpService
{
    public Task<T> GetAsync<T>(string url, Dictionary<string, string>? headers = null);
    public Task<T> PostAsync<T>(string url, object? body = null, Dictionary<string, string>? headers = null);
}
