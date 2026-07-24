using LEB2SCRAPPER.Entity.Models.Activity;

namespace LEB2SCRAPPER.Entity.Models.Response;

public sealed class SemesterSnapshotResponse
{
    public int SemesterId { get; set; }

    public List<SemesterSnapshotClass> Classes { get; set; } = new();
}

public sealed class SemesterSnapshotClass
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<Activity.Activity> Activities { get; set; } = new();
}
