using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Presentation.Legacy;

namespace LEB2SCRAPPER.Tests.Presentation;

public class ActivityLegacyExtensionsTests
{
    // Same instant as the parser tests: 2026-08-08T16:00:00Z == 2026-08-08T23:00:00+07:00.
    private static readonly DateTime DueDateUtc = new(2026, 8, 8, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ToLegacyV1_ShiftsUtcDueDateBackToGmtPlus7AndStripsKind()
    {
        var activity = new Activity { DueDate = DueDateUtc };

        var legacy = activity.ToLegacyV1();

        Assert.Equal(new DateTime(2026, 8, 8, 23, 0, 0), legacy.DueDate);
        Assert.Equal(DateTimeKind.Unspecified, legacy.DueDate!.Value.Kind);
    }

    [Fact]
    public void ToLegacyV1_NullDueDate_StaysNull()
    {
        var activity = new Activity { DueDate = null };

        Assert.Null(activity.ToLegacyV1().DueDate);
    }

    [Fact]
    public void ToLegacyV1_PreservesNonDateFields()
    {
        var activity = new Activity
        {
            Id = 42,
            ClassId = 7,
            Title = "Example assignment",
            DueDateExceed = true,
            Questions = [1, 2, 3]
        };

        var legacy = activity.ToLegacyV1();

        Assert.Equal(42, legacy.Id);
        Assert.Equal(7, legacy.ClassId);
        Assert.Equal("Example assignment", legacy.Title);
        Assert.True(legacy.DueDateExceed);
        Assert.Equal([1, 2, 3], legacy.Questions);
    }

    [Fact]
    public void ToLegacyV1_DoesNotMutateTheOriginalActivity()
    {
        var activity = new Activity { DueDate = DueDateUtc };

        activity.ToLegacyV1();

        Assert.Equal(DueDateUtc, activity.DueDate);
        Assert.Equal(DateTimeKind.Utc, activity.DueDate!.Value.Kind);
    }

    [Fact]
    public void ToLegacyV1_SemesterSnapshotResponse_MapsNestedActivities()
    {
        var response = new SemesterSnapshotResponse
        {
            SemesterId = 10,
            Classes =
            [
                new SemesterSnapshotClass
                {
                    Id = 20,
                    Name = "Example Class",
                    Activities = [new Activity { Id = 1, DueDate = DueDateUtc }]
                }
            ]
        };

        var legacy = response.ToLegacyV1();

        Assert.Equal(10, legacy.SemesterId);
        var legacyActivity = Assert.Single(legacy.Classes);
        Assert.Equal(20, legacyActivity.Id);
        var mappedDueDate = Assert.Single(legacyActivity.Activities).DueDate;
        Assert.Equal(new DateTime(2026, 8, 8, 23, 0, 0), mappedDueDate);
        Assert.Equal(DateTimeKind.Unspecified, mappedDueDate!.Value.Kind);
    }
}
