namespace LEB2SCRAPPER.Infrastructure.Contracts.Outbound;

public interface IOutboundRequestGate
{
    Task<T> ExecuteAsync<T>(
        OutboundRequestContext context,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
