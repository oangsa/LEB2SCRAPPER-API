namespace LEB2SCRAPPER.Entity.Models.Response;

public class ValidationErrorResponse
{
    public int StatusCode { get; set; } = 400;
    public string Message { get; set; } = string.Empty;
    public string ResponseCode { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? TraceId { get; set; }
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}
