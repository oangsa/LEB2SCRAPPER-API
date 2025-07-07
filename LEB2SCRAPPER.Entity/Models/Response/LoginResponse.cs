namespace LEB2SCRAPPER.Entity.Models.Response;

public class LoginResponse
{
    public bool Success { get; set; }
    public Result? Result { get; set; }
    public string? Token { get; set; }
    public string? RememberToken { get; set; }

}

public class Result
{
    public string StudentId { get; set; } = string.Empty;
    public string RadiusExpiration { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string KmuttUid { get; set; } = string.Empty;
    public List<string> Uids { get; set; } = new();
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string TitlenameTh { get; set; } = string.Empty;
    public string TitlenameEn { get; set; } = string.Empty;
    public string FirstnameTh { get; set; } = string.Empty;
    public string LastnameTh { get; set; } = string.Empty;
    public string FirstnameEn { get; set; } = string.Empty;
    public string LastnameEn { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string UniversityId { get; set; } = string.Empty;
    public string? ImagePath { get; set; } = null;
}
