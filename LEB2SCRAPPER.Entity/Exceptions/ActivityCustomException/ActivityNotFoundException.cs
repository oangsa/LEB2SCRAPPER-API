namespace LEB2SCRAPPER.Entity.Exceptions.ActivityCustomException;

public class ActivityNotFoundException : Exception
{
    public ActivityNotFoundException(string message) : base(message)
    {
    }
    public ActivityNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
