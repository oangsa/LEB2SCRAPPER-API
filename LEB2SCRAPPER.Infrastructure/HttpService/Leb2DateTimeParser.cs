using System.Globalization;
using System.Text.RegularExpressions;
using LEB2SCRAPPER.Entity.Models.Activity;

namespace LEB2SCRAPPER.Infrastructure.HttpService;

public static class Leb2DateTimeParser
{
    private static readonly string[] TimeZonelessFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd",
        "MM/dd/yyyy",
        "MM/dd/yyyy HH:mm:ss",
        "dd/MM/yyyy",
        "dd/MM/yyyy HH:mm:ss"
    };

    private static readonly Regex ExplicitTimeZonePattern = new(
        @"(?:Z|[+-]\d{2}:?\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (ExplicitTimeZonePattern.IsMatch(trimmed))
        {
            return DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var withOffset)
                ? withOffset.UtcDateTime
                : null;
        }

        foreach (var format in TimeZonelessFormats)
        {
            if (DateTime.TryParseExact(
                trimmed,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
            {
                return AssumeLeb2Offset(exact);
            }
        }

        if (DateTime.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var fallback))
        {
            return AssumeLeb2Offset(fallback);
        }

        return null;
    }

    public static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => AssumeLeb2Offset(value)
        };
    }

    public static string FormatForTransport(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            DateTimeKind.Local => new DateTimeOffset(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            _ => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture)
        };
    }

    private static DateTime AssumeLeb2Offset(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(unspecified - Leb2TimeZone.Offset, DateTimeKind.Utc);
    }
}
