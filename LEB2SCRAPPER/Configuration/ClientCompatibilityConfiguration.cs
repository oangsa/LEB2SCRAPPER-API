using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;

namespace LEB2SCRAPPER.Configuration;

public sealed class ClientCompatibilityConfiguration
{
    private ClientCompatibilityConfiguration(
        ClientCompatibilityOptions options,
        SemanticVersion minimumVersion,
        string downloadUrl)
    {
        EnforcementEnabled = options.EnforcementEnabled;
        MinimumClientVersion = options.MinimumClientVersion.Trim();
        LatestClientVersion = options.LatestClientVersion.Trim();
        DownloadUrl = downloadUrl;
        MinimumVersion = minimumVersion;
    }

    public bool EnforcementEnabled { get; }

    public string MinimumClientVersion { get; }

    public string LatestClientVersion { get; }

    public string DownloadUrl { get; }

    internal SemanticVersion MinimumVersion { get; }

    internal static ClientCompatibilityConfiguration Create(
        ClientCompatibilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!SemanticVersion.TryParse(
                options.MinimumClientVersion,
                out var minimumVersion))
        {
            throw new InvalidOperationException(
                "ClientCompatibility:MinimumClientVersion is invalid.");
        }

        if (!SemanticVersion.TryParse(
                options.LatestClientVersion,
                out var latestVersion))
        {
            throw new InvalidOperationException(
                "ClientCompatibility:LatestClientVersion is invalid.");
        }

        if (minimumVersion > latestVersion)
        {
            throw new InvalidOperationException(
                "ClientCompatibility:MinimumClientVersion must not exceed "
                + "ClientCompatibility:LatestClientVersion.");
        }

        var downloadUrl = options.DownloadUrl?.Trim();

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                "ClientCompatibility:DownloadUrl must be an absolute HTTP or HTTPS URL.");
        }

        return new ClientCompatibilityConfiguration(
            options,
            minimumVersion,
            downloadUrl);
    }
}

internal readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string[] PreRelease) : IComparable<SemanticVersion>
{
    public static bool TryParse(string value, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var withoutBuild = value.Trim().Split('+', 2)[0];
        var coreAndPreRelease = withoutBuild.Split('-', 2);
        var core = coreAndPreRelease[0].Split('.');

        if (core.Length != 3
            || !TryParseCorePart(core[0], out var major)
            || !TryParseCorePart(core[1], out var minor)
            || !TryParseCorePart(core[2], out var patch))
        {
            return false;
        }

        var preRelease = coreAndPreRelease.Length == 1
            ? Array.Empty<string>()
            : coreAndPreRelease[1].Split('.');

        if (preRelease.Any(string.IsNullOrEmpty)
            || preRelease.Any(identifier =>
                identifier.All(char.IsDigit)
                && identifier.Length > 1
                && identifier[0] == '0'))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var coreComparison = CompareCore(other);

        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (PreRelease.Length == 0 && other.PreRelease.Length == 0)
        {
            return 0;
        }

        if (PreRelease.Length == 0)
        {
            return 1;
        }

        if (other.PreRelease.Length == 0)
        {
            return -1;
        }

        for (var index = 0;
             index < Math.Min(PreRelease.Length, other.PreRelease.Length);
             index++)
        {
            var comparison = CompareIdentifier(
                PreRelease[index],
                other.PreRelease[index]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return PreRelease.Length.CompareTo(other.PreRelease.Length);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    private int CompareCore(SemanticVersion other)
    {
        return Major != other.Major
            ? Major.CompareTo(other.Major)
            : Minor != other.Minor
                ? Minor.CompareTo(other.Minor)
                : Patch.CompareTo(other.Patch);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = int.TryParse(left, out var leftNumber);
        var rightNumeric = int.TryParse(right, out var rightNumber);

        if (leftNumeric && rightNumeric)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool TryParseCorePart(string value, out int result)
    {
        result = 0;
        return value.Length > 0
            && (value == "0" || value[0] != '0')
            && int.TryParse(value, out result)
            && result >= 0;
    }
}
