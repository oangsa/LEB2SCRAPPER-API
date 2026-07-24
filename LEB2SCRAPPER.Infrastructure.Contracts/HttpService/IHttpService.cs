using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;

namespace LEB2SCRAPPER.Infrastructure.Contracts.HttpService;

public interface IHttpService
{
    Task<T> GetAsync<T>(
        string url,
        OutboundRequestContext context,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<T> PostAsync<T>(
        string url,
        object? body,
        OutboundRequestContext context,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
