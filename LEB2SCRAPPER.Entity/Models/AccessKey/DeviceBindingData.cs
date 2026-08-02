namespace LEB2SCRAPPER.Entity.Models.AccessKey;

public sealed record DeviceBindingData(
    string DeviceIdHash,
    string? DeviceName,
    string? Platform,
    string? OsVersion,
    string? AppVersion);
