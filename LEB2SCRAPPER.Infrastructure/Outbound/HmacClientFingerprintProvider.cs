using System.Security.Cryptography;
using System.Text;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;

namespace LEB2SCRAPPER.Infrastructure.Outbound;

public sealed class HmacClientFingerprintProvider : IClientFingerprintProvider, IDisposable
{
    private static readonly byte[] SessionDomain = "leb2-session\0"u8.ToArray();
    private static readonly byte[] UsernameDomain = "leb2-username\0"u8.ToArray();
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string CreateForSession(string sessionValue)
    {
        if (string.IsNullOrWhiteSpace(sessionValue))
        {
            throw new ArgumentException(
                "Session value must be provided.",
                nameof(sessionValue));
        }

        return CreateFingerprint(SessionDomain, sessionValue);
    }

    public string CreateForUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username must be provided.", nameof(username));
        }

        var normalizedUsername = username
            .Trim()
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();

        return CreateFingerprint(UsernameDomain, normalizedUsername);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
    }

    private string CreateFingerprint(byte[] domain, string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var input = new byte[domain.Length + valueBytes.Length];

        domain.CopyTo(input, 0);
        valueBytes.CopyTo(input, domain.Length);

        try
        {
            using var hmac = new HMACSHA256(_key);
            return Convert.ToHexString(hmac.ComputeHash(input));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(valueBytes);
            CryptographicOperations.ZeroMemory(input);
        }
    }
}
