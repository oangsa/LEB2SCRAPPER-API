using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;

namespace LEB2SCRAPPER.Infrastructure.HttpService;

public class HttpService : IHttpService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public HttpService()
    {
        _httpClient = new HttpClient();
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
    public async Task<T> GetAsync<T>(string url, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        try
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    if (string.IsNullOrWhiteSpace(header.Value))
                        continue;

                    request.Headers.Add(header.Key, header.Value);
                }
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new HttpRequestException($"No content returned from GET {url}");
            }

            var result = JsonSerializer.Deserialize<T>(content, _jsonOptions);

            if (Equals(result, default(T)) || result is null)
            {
                throw new HttpRequestException($"Failed to deserialize response from GET {url}");
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new HttpRequestException($"Failed to GET {url}: {ex.Message}");
        }
    }

    public async Task<T> PostAsync<T>(string url, object data, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        var content = JsonSerializer.Serialize(data, _jsonOptions);
        request.Content = new StringContent(content, Encoding.UTF8, "application/json");

        try
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    if (string.IsNullOrWhiteSpace(header.Value))
                        continue;

                    request.Headers.Add(header.Key, header.Value);
                }
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<T>(responseContent, _jsonOptions);

            if (Equals(result, default(T)) || result is null)
            {
                throw new HttpRequestException($"Failed to deserialize response from POST {url}");
            }

            return result;

        }
        catch (Exception ex)
        {
            throw new HttpRequestException($"Failed to POST {url}: {ex.Message}");
        }
    }

        public class FlexibleDateTimeConverter : JsonConverter<DateTime?>
{
    private readonly string[] _dateFormats = new[]
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-dd",
        "MM/dd/yyyy",
        "MM/dd/yyyy HH:mm:ss",
        "dd/MM/yyyy",
        "dd/MM/yyyy HH:mm:ss"
    };

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var dateString = reader.GetString();

            if (string.IsNullOrEmpty(dateString))
            {
                return null;
            }

            foreach (var format in _dateFormats)
            {
                if (DateTime.TryParseExact(dateString, format, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result))
                {
                    return result;
                }
            }

            if (DateTime.TryParse(dateString, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fallbackResult))
            {
                return fallbackResult;
            }

            return null;
        }

        try
        {
            return reader.GetDateTime();
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
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

    public class FlexibleNonNullableDateTimeConverter : JsonConverter<DateTime>
    {
        private readonly string[] _dateFormats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "MM/dd/yyyy HH:mm:ss",
            "dd/MM/yyyy",
            "dd/MM/yyyy HH:mm:ss"
        };

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return DateTime.MinValue;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var dateString = reader.GetString();

                if (string.IsNullOrEmpty(dateString))
                {
                    return DateTime.MinValue;
                }

                foreach (var format in _dateFormats)
                {
                    if (DateTime.TryParseExact(dateString, format, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result))
                    {
                        return result;
                    }
                }

                if (DateTime.TryParse(dateString, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fallbackResult))
                {
                    return fallbackResult;
                }

                return DateTime.MinValue;
            }

            try
            {
                return reader.GetDateTime();
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
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
