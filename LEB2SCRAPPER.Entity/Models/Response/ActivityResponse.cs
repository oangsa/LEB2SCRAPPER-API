using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.Users;

namespace LEB2SCRAPPER.Entity.Models.Response;

public class ActivityResponse
{
    public List<Activity.Activity> Activities { get; set; } = new();
    public List<User> User { get; set; } = new();
}
