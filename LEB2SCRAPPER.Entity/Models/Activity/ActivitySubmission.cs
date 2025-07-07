namespace LEB2SCRAPPER.Entity.Models.Activity;

public class ActivitySubmission
{
    public string Date { get; set; } = string.Empty;
    public int TimezoneType { get; set; }
    public string Timezone { get; set; } = string.Empty;
}
