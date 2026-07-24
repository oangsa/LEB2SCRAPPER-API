namespace LEB2SCRAPPER.Infrastructure.Contracts.Outbound;

public sealed record OutboundRequestContext(
    string Endpoint,
    bool UsesSessionCredential = false);
