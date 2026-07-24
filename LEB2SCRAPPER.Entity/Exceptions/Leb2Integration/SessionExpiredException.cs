namespace LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;

public class SessionExpiredException : Exception
{
    public SessionExpiredException()
        : base("The LEB2 session has expired or is invalid.")
    {
    }
}
