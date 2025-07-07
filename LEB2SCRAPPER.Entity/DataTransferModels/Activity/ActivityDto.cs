namespace LEB2SCRAPPER.Entity.DataTransferModels.Activity;

public class ActivityDto
{
    public int UserId { get; set; }
    public int ClassId { get; set; }

    public List<Models.Activity.Activity>? activities { get; set; }
}
