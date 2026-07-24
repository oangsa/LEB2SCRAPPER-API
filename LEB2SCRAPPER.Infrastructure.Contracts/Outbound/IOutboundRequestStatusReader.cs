namespace LEB2SCRAPPER.Infrastructure.Contracts.Outbound;

public interface IOutboundRequestStatusReader
{
    OutboundRequestStatusSnapshot GetSnapshot();
}

public sealed record OutboundRequestStatusSnapshot(
    DateTimeOffset ObservedAt,
    IReadOnlyList<OutboundEndpointStatus> Endpoints);

public sealed record OutboundEndpointStatus(
    string Name,
    DateTimeOffset? RetryAt,
    int RetryAfterSeconds);
