using System.Text.Json;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;
using Microsoft.AspNetCore.Authorization;

namespace LEB2SCRAPPER.Middleware;

public sealed class ClientCompatibilityMiddleware
{
    public const string ClientVersionHeaderName = "X-Client-Version";

    private readonly RequestDelegate _next;
    private readonly ClientCompatibilityOptions _options;

    public ClientCompatibilityMiddleware(
        RequestDelegate next,
        ClientCompatibilityOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnforcementEnabled
            || context.GetEndpoint() is null
            || context.GetEndpoint()!.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        var values = context.Request.Headers[ClientVersionHeaderName];

        if (values.Count == 0 || string.IsNullOrWhiteSpace(values[0]))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ClientVersionRequired,
                "A client version is required.");
            return;
        }

        if (values.Count != 1
            || !SemanticVersion.TryParse(values[0]!, out var clientVersion)
            || !SemanticVersion.TryParse(
                _options.MinimumClientVersion,
                out var minimumVersion))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.InvalidRequest,
                "The client version is invalid.");
            return;
        }

        if (clientVersion < minimumVersion)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status426UpgradeRequired,
                ApiErrorCodes.ClientUpdateRequired,
                $"This client version is no longer supported. Update to {_options.MinimumClientVersion}.");
            return;
        }

        await _next(context);
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string responseCode,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var response = new ErrorResponse
        {
            Message = message,
            ResponseCode = responseCode,
            TraceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
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
