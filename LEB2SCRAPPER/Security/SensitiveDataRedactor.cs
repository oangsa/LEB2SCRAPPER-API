using System.Text.RegularExpressions;

namespace LEB2SCRAPPER.Security;

public static partial class SensitiveDataRedactor
{
    public static string Redact(string value, params string?[] secrets)
    {
        var redacted = AuthorizationHeaderRegex().Replace(value, "$1[REDACTED]");
        redacted = CookieHeaderRegex().Replace(redacted, "$1[REDACTED]");

        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    [GeneratedRegex(@"(?i)(Authorization\s*[:=]\s*Bearer\s+)[^\r\n]+")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"(?i)(Cookie\s*[:=]\s*)[^\r\n]+")]
    private static partial Regex CookieHeaderRegex();
}
