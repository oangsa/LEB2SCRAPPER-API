namespace LEB2SCRAPPER.Infrastructure.Outbound;

public sealed class OutboundRequestGateOptions
{
    public const int DefaultStructuralFailureThreshold = 3;
    public const int DefaultStructuralFailureWindowMinutes = 15;

    public int MaxConcurrentRequests { get; set; } = 4;
    public int BaseBackoffSeconds { get; set; } = 30;
    public int MaxBackoffMinutes { get; set; } = 5;
    public int FailureResetMinutes { get; set; } = 15;
    public int StructuralFailureThreshold { get; set; } = DefaultStructuralFailureThreshold;
    public int StructuralFailureWindowMinutes { get; set; } = DefaultStructuralFailureWindowMinutes;
}
