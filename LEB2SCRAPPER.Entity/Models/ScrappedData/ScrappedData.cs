using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Users;

namespace LEB2SCRAPPER.Entity.Models.ScrappedData;

public class ScrappedData
{
    public string Cookies { get; set; } = string.Empty;
    public List<int> SemesterIds { get; set; } = new();
    public Dictionary<string, List<ClassInfo>> Classes { get; set; } = new();
    public User? User { get; set; }
}
