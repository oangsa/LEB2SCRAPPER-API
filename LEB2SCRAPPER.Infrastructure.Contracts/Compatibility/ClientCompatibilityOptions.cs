namespace LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;

public sealed class ClientCompatibilityOptions
{
    public bool EnforcementEnabled { get; set; }

    public string MinimumClientVersion { get; set; } = "0.5.0";

    public string LatestClientVersion { get; set; } = "0.5.0";

    public string DownloadUrl { get; set; } =
        "https://github.com/oangsa/leb2-watch/releases/latest";
}
