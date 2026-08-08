using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;

namespace LEB2SCRAPPER.Infrastructure.HttpService;

public class HttpService : IHttpService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IOutboundRequestGate _outboundRequestGate;

    public HttpService(
        HttpClient httpClient,
        IOutboundRequestGate outboundRequestGate)
    {
        _httpClient = httpClient;
        _outboundRequestGate = outboundRequestGate;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            Converters =
            {
                new FlexibleDateTimeConverter(),
                new FlexibleNonNullableDateTimeConverter(),
                new FlexibleBooleanConverter(),
                new FlexibleNullableBooleanConverter()
            }
        };
    }

    public Task<T> GetAsync<T>(
        string url,
        OutboundRequestContext context,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        return _outboundRequestGate.ExecuteAsync(
            context,
            token => SendAsync<T>(HttpMethod.Get, url, null, context, headers, token),
            cancellationToken);
    }

    public Task<T> PostAsync<T>(
        string url,
        object? body,
        OutboundRequestContext context,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        return _outboundRequestGate.ExecuteAsync(
            context,
            token => SendAsync<T>(HttpMethod.Post, url, body, context, headers, token),
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string url,
        object? body,
        OutboundRequestContext context,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            var serializedBody = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");
        }

        AddHeaders(request, headers);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransientLeb2Exception("The LEB2 request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TransientLeb2Exception("The LEB2 request could not connect.", exception);
        }

        using (response)
        {
            if (IsExpiredSessionResponse(response, context))
            {
                throw new SessionExpiredException();
            }

            if (IsTransientStatus(response.StatusCode))
            {
                throw new TransientLeb2Exception(
                    $"LEB2 returned transient HTTP status {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Leb2UpstreamException(
                    $"LEB2 returned HTTP status {(int)response.StatusCode}.");
            }

            string responseContent;

            try
            {
                responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TransientLeb2Exception(
                    "The LEB2 response timed out while being read.",
                    exception);
            }
            catch (HttpRequestException exception)
            {
                throw new TransientLeb2Exception(
                    "The LEB2 response could not be read.",
                    exception);
            }

            if (LooksLikeHtml(response, responseContent))
            {
                if (context.UsesSessionCredential && LooksLikeLoggedOutHtml(responseContent))
                {
                    throw new SessionExpiredException();
                }

                throw new StructuralParseException(
                    $"{context.Endpoint}.unexpected_html",
                    "LEB2 returned HTML where structured data was expected.");
            }

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                throw new StructuralParseException(
                    $"{context.Endpoint}.empty_response",
                    "LEB2 returned an empty response.");
            }

            try
            {
                var result = JsonSerializer.Deserialize<T>(responseContent, _jsonOptions);

                if (Equals(result, default(T)) || result is null)
                {
                    throw new StructuralParseException(
                        $"{context.Endpoint}.empty_json_result",
                        "LEB2 returned an empty JSON result.");
                }

                return result;
            }
            catch (JsonException exception)
            {
                throw new StructuralParseException(
                    $"{context.Endpoint}.json_shape",
                    "LEB2 returned an unexpected JSON response shape.",
                    exception);
            }
        }
    }

    private static void AddHeaders(
        HttpRequestMessage request,
        Dictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var header in headers)
        {
            if (!string.IsNullOrWhiteSpace(header.Value))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    private static bool IsExpiredSessionResponse(
        HttpResponseMessage response,
        OutboundRequestContext context)
    {
        if (!context.UsesSessionCredential)
        {
            return false;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return true;
        }

        if ((int)response.StatusCode is < 300 or >= 400)
        {
            return false;
        }

        var location = response.Headers.Location;

        if (location is null)
        {
            return false;
        }

        if (!location.IsAbsoluteUri)
        {
            return location.OriginalString.Contains("login", StringComparison.OrdinalIgnoreCase);
        }

        return location.Host.Equals("www.leb2.org", StringComparison.OrdinalIgnoreCase)
            || location.Host.Equals("signin.leb2.org", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private static bool LooksLikeHtml(
        HttpResponseMessage response,
        string content)
    {
        return response.Content.Headers.ContentType?.MediaType?.Equals(
                   "text/html",
                   StringComparison.OrdinalIgnoreCase) == true
            || content.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeLoggedOutHtml(string content)
    {
        return content.Contains("https://www.leb2.org/", StringComparison.OrdinalIgnoreCase)
            || content.Contains("signin.leb2.org", StringComparison.OrdinalIgnoreCase);
    }

    public class FlexibleDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return Leb2DateTimeParser.Parse(reader.GetString());
            }

            try
            {
                return Leb2DateTimeParser.NormalizeToUtc(reader.GetDateTime());
            }
            catch
            {
                return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteStringValue(Leb2DateTimeParser.FormatForTransport(value.Value));
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    public class FlexibleNonNullableDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return DateTime.MinValue;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return Leb2DateTimeParser.Parse(reader.GetString()) ?? DateTime.MinValue;
            }

            try
            {
                return Leb2DateTimeParser.NormalizeToUtc(reader.GetDateTime());
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(Leb2DateTimeParser.FormatForTransport(value));
        }
    }

    public class FlexibleBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number:
                    // Handle 0/1 as false/true
                    if (reader.TryGetInt32(out var intValue))
                    {
                        return intValue != 0;
                    }
                    if (reader.TryGetDouble(out var doubleValue))
                    {
                        return Math.Abs(doubleValue) > 0.001;
                    }
                    return false;
                case JsonTokenType.String:
                    // Handle string representations
                    var stringValue = reader.GetString();
                    if (bool.TryParse(stringValue, out var boolResult))
                    {
                        return boolResult;
                    }
                    // Handle "0"/"1" as strings
                    if (stringValue == "1" || stringValue?.ToLowerInvariant() == "true")
                    {
                        return true;
                    }
                    if (stringValue == "0" || stringValue?.ToLowerInvariant() == "false")
                    {
                        return false;
                    }
                    return false;
                case JsonTokenType.Null:
                    return false;
                default:
                    return false;
            }
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }
}

    public class FlexibleNullableBooleanConverter : JsonConverter<bool?>
    {
            public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.True:
                        return true;
                    case JsonTokenType.False:
                        return false;
                    case JsonTokenType.Number:
                        if (reader.TryGetInt32(out var intValue))
                        {
                            return intValue != 0;
                        }
                        if (reader.TryGetDouble(out var doubleValue))
                        {
                            return Math.Abs(doubleValue) > 0.001;
                        }
                        return false;
                    case JsonTokenType.String:
                        var stringValue = reader.GetString();
                        if (string.IsNullOrEmpty(stringValue))
                        {
                            return null;
                        }
                        if (bool.TryParse(stringValue, out var boolResult))
                        {
                            return boolResult;
                        }
                        // Handle "0"/"1" as strings
                        if (stringValue == "1" || stringValue.ToLowerInvariant() == "true")
                        {
                            return true;
                        }
                        if (stringValue == "0" || stringValue.ToLowerInvariant() == "false")
                        {
                            return false;
                        }
                        return null;
                    case JsonTokenType.Null:
                        return null;
                    default:
                        return null;
                }
            }

            public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
            {
                if (value.HasValue)
                {
                    writer.WriteBooleanValue(value.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }
        }
}
