using System.Text.Json;
using LEB2SCRAPPER.Infrastructure.HttpService;

namespace LEB2SCRAPPER.Tests.Infrastructure;

public class Leb2DateTimeParserTests
{
    // Upstream LEB2 due_date "2026-08-08 23:00:00" (no offset) is contractually GMT+7,
    // which is the same instant as 2026-08-08T16:00:00Z.
    private static readonly DateTime ExpectedUtcInstant =
        new(2026, 8, 8, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Parse_TimezonelessValue_IsInterpretedAsGmtPlus7()
    {
        var result = Leb2DateTimeParser.Parse("2026-08-08 23:00:00");

        Assert.Equal(ExpectedUtcInstant, result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Fact]
    public void Parse_ExplicitUtcValue_IsRespectedAsIs()
    {
        var result = Leb2DateTimeParser.Parse("2026-08-08T16:00:00Z");

        Assert.Equal(ExpectedUtcInstant, result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Fact]
    public void Parse_ExplicitOffsetValue_IsNotDoubleShifted()
    {
        var result = Leb2DateTimeParser.Parse("2026-08-08T23:00:00+07:00");

        Assert.Equal(ExpectedUtcInstant, result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Theory]
    [InlineData("2026-08-08 23:00:00")]
    [InlineData("2026-08-08T16:00:00Z")]
    [InlineData("2026-08-08T23:00:00+07:00")]
    public void Parse_AllThreeUpstreamRepresentations_ProduceTheSameInstant(string upstreamValue)
    {
        Assert.Equal(ExpectedUtcInstant, Leb2DateTimeParser.Parse(upstreamValue));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_MissingValue_ReturnsNull(string? value)
    {
        Assert.Null(Leb2DateTimeParser.Parse(value));
    }

    [Fact]
    public void FormatForTransport_UtcValue_UsesGenuineZSuffix()
    {
        var formatted = Leb2DateTimeParser.FormatForTransport(ExpectedUtcInstant);

        Assert.Equal("2026-08-08T16:00:00.000Z", formatted);
    }

    [Fact]
    public void FormatForTransport_UnspecifiedKindValue_IsNeverLabeledAsUtc()
    {
        var unspecified = new DateTime(2026, 8, 8, 23, 0, 0, DateTimeKind.Unspecified);

        var formatted = Leb2DateTimeParser.FormatForTransport(unspecified);

        Assert.DoesNotContain("Z", formatted);
        Assert.Equal("2026-08-08T23:00:00.000", formatted);
    }

    [Fact]
    public void RoundTrip_TimezonelessValue_DoesNotDriftBySevenHours()
    {
        var firstParse = Leb2DateTimeParser.Parse("2026-08-08 23:00:00")!.Value;
        var transported = Leb2DateTimeParser.FormatForTransport(firstParse);
        var secondParse = Leb2DateTimeParser.Parse(transported);

        Assert.Equal(firstParse, secondParse);
    }

    [Fact]
    public void JsonConverter_RoundTripsThroughHttpServiceJsonOptions_PreservesInstant()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new HttpService.FlexibleDateTimeConverter() }
        };

        var deserialized = JsonSerializer.Deserialize<DateTime?>(
            "\"2026-08-08 23:00:00\"",
            options);
        var serialized = JsonSerializer.Serialize(deserialized, options);
        var reDeserialized = JsonSerializer.Deserialize<DateTime?>(serialized, options);

        Assert.Equal(ExpectedUtcInstant, deserialized);
        Assert.Equal("\"2026-08-08T16:00:00.000Z\"", serialized);
        Assert.Equal(ExpectedUtcInstant, reDeserialized);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Bangkok")]
    [InlineData("America/New_York")]
    [InlineData("Pacific/Kiritimati")]
    public void Parse_IsIndependentOfHostLocalTimeZone(string ianaTimeZoneId)
    {
        RunUnderTimeZone(ianaTimeZoneId, () =>
        {
            Assert.Equal(
                ExpectedUtcInstant,
                Leb2DateTimeParser.Parse("2026-08-08 23:00:00"));
            Assert.Equal(
                ExpectedUtcInstant,
                Leb2DateTimeParser.Parse("2026-08-08T23:00:00+07:00"));
        });
    }

    private static void RunUnderTimeZone(string ianaTimeZoneId, Action assertions)
    {
        var originalTz = Environment.GetEnvironmentVariable("TZ");

        try
        {
            Environment.SetEnvironmentVariable("TZ", ianaTimeZoneId);
            TimeZoneInfo.ClearCachedData();

            assertions();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", originalTz);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
