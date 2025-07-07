namespace LEB2SCRAPPER.Entity.Models.Authentication;

public class Credentials
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public bool Remember { get; set; }
}
