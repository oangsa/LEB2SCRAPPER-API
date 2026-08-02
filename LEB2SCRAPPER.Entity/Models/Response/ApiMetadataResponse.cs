namespace LEB2SCRAPPER.Entity.Models.Response;

public sealed class ApiMetadataResponse
{
    public int ApiVersion { get; set; }

    public string MinimumClientVersion { get; set; } = string.Empty;

    public string LatestClientVersion { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;
}
