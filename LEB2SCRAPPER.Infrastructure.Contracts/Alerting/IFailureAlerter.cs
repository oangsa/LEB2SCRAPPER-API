namespace LEB2SCRAPPER.Infrastructure.Contracts.Alerting;

public interface IFailureAlerter
{
    Task NotifyStructuralFailureAsync(
        StructuralFailureAlert alert,
        CancellationToken cancellationToken = default);
}

public sealed record StructuralFailureAlert(
    string Endpoint,
    string FailureShape,
    int FailureCount,
    DateTimeOffset WindowStartedAt,
    DateTimeOffset DetectedAt);
