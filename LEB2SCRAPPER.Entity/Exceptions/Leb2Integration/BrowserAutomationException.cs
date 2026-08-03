namespace LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;

public sealed class BrowserAutomationException : Exception
{
    public BrowserAutomationException(
        string stage,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Stage = stage;
    }

    public string Stage { get; }
}
