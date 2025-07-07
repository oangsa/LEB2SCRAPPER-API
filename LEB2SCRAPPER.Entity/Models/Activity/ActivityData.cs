using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.Users;

namespace LEB2SCRAPPER.Entity.Models.Activity;
public class ActivityData
{
    public List<Activity> Activities { get; set; } = new();
    public List<User> Users { get; set; } = new();
    public Dictionary<string, object> Meta { get; set; } = new();
}
