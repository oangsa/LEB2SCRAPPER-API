namespace LEB2SCRAPPER.Entity.Models.Users;

public class User
{
    public int Id { get; set; }
    public string KmuttId { get; set; } = string.Empty;
    public string NameThai { get; set; } = string.Empty;
    public string NameEnglish { get; set; } = string.Empty;
    public string SurnameThai { get; set; } = string.Empty;
    public string SurnameEnglish { get; set; } = string.Empty;
}
