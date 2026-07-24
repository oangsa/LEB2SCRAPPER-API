namespace LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;

public class OutboundRequestBackoffException : Exception
{
    public OutboundRequestBackoffException(DateTimeOffset retryAt)
        : base("LEB2 access is temporarily paused after recent failures.")
    {
        RetryAt = retryAt;
    }

    public DateTimeOffset RetryAt { get; }
}
