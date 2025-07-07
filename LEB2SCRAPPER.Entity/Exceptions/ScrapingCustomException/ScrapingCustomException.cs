namespace LEB2SCRAPPER.Entity.Exceptions.ScrapingCustomException;

public class ScrapingCustomException : Exception
{
    public ScrapingCustomException(string message) : base(message)
    {
    }
    public ScrapingCustomException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
