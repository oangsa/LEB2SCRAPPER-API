namespace LEB2SCRAPPER.Entity.Models.AccessKey;

public sealed record DeviceBindingRequest(
    string DeviceId,
    string? DeviceName,
    string? Platform,
    string? OsVersion,
    string? AppVersion);
