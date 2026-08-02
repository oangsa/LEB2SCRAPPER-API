using System.Security.Cryptography;
using System.Text;

namespace LEB2SCRAPPER.Service.Master;

internal static class DeviceIdHasher
{
    public static string Hash(string secret, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "Device-binding HMAC secret is not configured.");
        }

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var deviceBytes = Encoding.UTF8.GetBytes(deviceId);
        var hash = HMACSHA256.HashData(secretBytes, deviceBytes);
        return Convert.ToHexString(hash);
    }
}
