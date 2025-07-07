namespace LEB2SCRAPPER.Entity.Exceptions.ActivityCustomException;

public class ActivityCustomExceptionException : Exception
{
    public ActivityCustomExceptionException(string message) : base(message)
    {
    }

    public ActivityCustomExceptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
