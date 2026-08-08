using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;
using Microsoft.Extensions.Logging;

namespace LEB2SCRAPPER.Infrastructure.Compatibility;

public sealed class GithubLatestClientVersionProvider : ILatestClientVersionProvider
{
    private const string ReleasePath = "repos/oangsa/leb2-watch/releases/latest";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GithubLatestClientVersionProvider> _logger;
    private readonly object _cacheLock = new();
    private string? _cachedVersion;
    private DateTimeOffset _cacheExpiresAt;

    public GithubLatestClientVersionProvider(
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<GithubLatestClientVersionProvider> logger)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_cacheLock)
        {
            if (_cachedVersion is not null && now < _cacheExpiresAt)
            {
                return _cachedVersion;
            }
        }

        string? fetchedVersion;

        try
        {
            using var response = await _httpClient.GetAsync(ReleasePath, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GitHub release lookup for leb2-watch returned HTTP status {StatusCode}.",
                    (int)response.StatusCode);
                return GetStaleValueOrDefault();
            }

            var payload = await response.Content.ReadFromJsonAsync<GithubReleaseResponse>(
                cancellationToken: cancellationToken);
            fetchedVersion = NormalizeTag(payload?.TagName);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "GitHub release lookup for leb2-watch timed out.");
            return GetStaleValueOrDefault();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "GitHub release lookup for leb2-watch could not connect.");
            return GetStaleValueOrDefault();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "GitHub release lookup for leb2-watch returned an unexpected JSON shape.");
            return GetStaleValueOrDefault();
        }

        if (fetchedVersion is null)
        {
            return GetStaleValueOrDefault();
        }

        lock (_cacheLock)
        {
            _cachedVersion = fetchedVersion;
            _cacheExpiresAt = now + CacheTtl;
        }

        return fetchedVersion;
    }

    private string? GetStaleValueOrDefault()
    {
        // ponytail: on fetch failure this serves the last cached tag with no retry/backoff;
        // add jittered retry if GitHub outages start flapping meta responses.
        lock (_cacheLock)
        {
            return _cachedVersion;
        }
    }

    private static string? NormalizeTag(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var trimmed = tagName.Trim();
        return trimmed[0] is 'v' or 'V' ? trimmed[1..] : trimmed;
    }

    private sealed record GithubReleaseResponse(
        [property: JsonPropertyName("tag_name")] string? TagName);
}
