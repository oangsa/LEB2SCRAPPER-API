using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.ModelsExtension;

namespace LEB2SCRAPPER.Tests.Entity;

public class ActivityExtensionTests
{
    // 2026-08-08T23:00:00+07:00, the same instant as 2026-08-08T16:00:00Z.
    // Activity.DueDate is populated exclusively through Leb2DateTimeParser, so it is
    // always already a normalized UTC instant by the time GetStatusText runs.
    private static readonly DateTime DueDateUtc = new(2026, 8, 8, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StatusText_OneSecondBeforeUtcDeadline_ReturnsPending()
    {
        var activity = new Activity { DueDate = DueDateUtc };

        var status = activity.StatusText(DateTimeOffset.Parse("2026-08-08T15:59:59Z"));

        Assert.Equal("Pending", status);
    }

    [Fact]
    public void StatusText_OneSecondAfterUtcDeadline_NoLongerReturnsPending()
    {
        var activity = new Activity { DueDate = DueDateUtc };

        var status = activity.StatusText(DateTimeOffset.Parse("2026-08-08T16:00:01Z"));

        Assert.Equal("Available", status);
    }

    [Fact]
    public void StatusText_UpstreamDueDateExceedFlag_WinsRegardlessOfLocalComparison()
    {
        var activity = new Activity
        {
            DueDate = DueDateUtc,
            DueDateExceed = true
        };

        var status = activity.StatusText(DateTimeOffset.Parse("2000-01-01T00:00:00Z"));

        Assert.Equal("Overdue", status);
    }

    [Fact]
    public void StatusText_DefaultParameter_UsesRealUtcNow()
    {
        var pendingActivity = new Activity { DueDate = DateTime.UtcNow.AddDays(1) };
        var pastActivity = new Activity { DueDate = DateTime.UtcNow.AddDays(-1) };

        Assert.Equal("Pending", pendingActivity.StatusText());
        Assert.Equal("Available", pastActivity.StatusText());
    }

    // Guards against a future regression back to DateTime.Now: if GetStatusText ever
    // reads host-local time again, this fails under zones other than the one running it.
    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Bangkok")]
    [InlineData("America/New_York")]
    [InlineData("Pacific/Kiritimati")]
    public void StatusText_ComparisonResult_IsIndependentOfHostLocalTimeZone(string ianaTimeZoneId)
    {
        var originalTz = Environment.GetEnvironmentVariable("TZ");

        try
        {
            Environment.SetEnvironmentVariable("TZ", ianaTimeZoneId);
            TimeZoneInfo.ClearCachedData();

            var activity = new Activity { DueDate = DueDateUtc };

            Assert.Equal(
                "Pending",
                activity.StatusText(DateTimeOffset.Parse("2026-08-08T15:59:59Z")));
            Assert.Equal(
                "Available",
                activity.StatusText(DateTimeOffset.Parse("2026-08-08T16:00:01Z")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", originalTz);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
