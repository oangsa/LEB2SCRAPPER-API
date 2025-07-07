namespace LEB2SCRAPPER.Infrastructure.Contracts.HttpService;

public interface IHttpService
{
    Task<T> GetAsync<T>(string url, Dictionary<string, string>? headers = null);
    Task<T> PostAsync<T>(string url, object data, Dictionary<string, string>? headers = null);
}
