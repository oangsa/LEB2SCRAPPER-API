using System.ComponentModel.DataAnnotations;

namespace LEB2SCRAPPER.Entity.DataTransferModels.Activity;

public class ActivityDto
{
    [Required(ErrorMessage = "UserId is required")]
    [Range(1, int.MaxValue, ErrorMessage = "UserId must be greater than 0")]
    public int UserId { get; set; }

    [Required(ErrorMessage = "ClassId is required")]
    [Range(1, int.MaxValue, ErrorMessage = "ClassId must be greater than 0")]
    public int ClassId { get; set; }

    public List<Models.Activity.Activity>? activities { get; set; }
}
