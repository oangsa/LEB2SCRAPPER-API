using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.Response;

namespace LEB2SCRAPPER.Presentation.Legacy;

// api/v1 Activity responses are frozen to the pre-fix wire shape: due/start/created
// dates as naive GMT+7 wall-clock strings with no offset. api/v2 returns the
// corrected UTC instant. Activity.DueDate/StartDate/CreatedAt are always UTC
// internally (Leb2DateTimeParser); this only re-shapes the outbound v1 response.
public static class ActivityLegacyExtensions
{
    public static List<Activity> ToLegacyV1(this List<Activity> activities) =>
        activities.Select(activity => activity.ToLegacyV1()).ToList();

    public static Activity ToLegacyV1(this Activity activity)
    {
        var legacy = activity.Clone();
        legacy.StartDate = ToLegacyWallClock(activity.StartDate);
        legacy.DueDate = ToLegacyWallClock(activity.DueDate);
        legacy.CreatedAt = ToLegacyWallClock(activity.CreatedAt);
        legacy.LastDueDateNotificationDate = ToLegacyWallClock(activity.LastDueDateNotificationDate);
        legacy.LastStatusChangeNotificationDate = ToLegacyWallClock(activity.LastStatusChangeNotificationDate);
        return legacy;
    }

    public static SemesterSnapshotResponse ToLegacyV1(this SemesterSnapshotResponse response)
    {
        return new SemesterSnapshotResponse
        {
            SemesterId = response.SemesterId,
            Classes = response.Classes
                .Select(classInfo => new SemesterSnapshotClass
                {
                    Id = classInfo.Id,
                    Name = classInfo.Name,
                    Activities = classInfo.Activities.ToLegacyV1()
                })
                .ToList()
        };
    }

    private static DateTime ToLegacyWallClock(DateTime utcValue) =>
        DateTime.SpecifyKind(utcValue + Leb2TimeZone.Offset, DateTimeKind.Unspecified);

    private static DateTime? ToLegacyWallClock(DateTime? utcValue) =>
        utcValue.HasValue ? ToLegacyWallClock(utcValue.Value) : null;
}
