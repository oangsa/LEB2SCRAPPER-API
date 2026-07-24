namespace LEB2SCRAPPER.Entity.Models.Response;

public sealed class Leb2HealthResponse
{
    public DateTime ObservedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public List<Leb2EndpointHealthResponse> Endpoints { get; set; } = new();
}

public sealed class Leb2EndpointHealthResponse
{
    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime? RetryAt { get; set; }

    public int RetryAfterSeconds { get; set; }
}
