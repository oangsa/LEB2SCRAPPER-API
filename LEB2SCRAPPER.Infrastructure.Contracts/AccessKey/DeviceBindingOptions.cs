namespace LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;

public sealed class DeviceBindingOptions
{
    public bool Enabled { get; set; }

    public bool EnforcementEnabled { get; set; }

    public string HmacSecret { get; set; } = string.Empty;
}
