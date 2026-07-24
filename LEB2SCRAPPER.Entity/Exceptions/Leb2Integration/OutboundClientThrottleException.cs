namespace LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;

public class OutboundClientThrottleException : Exception
{
    public OutboundClientThrottleException(DateTimeOffset retryAt)
        : base("This client has too many queued LEB2 requests.")
    {
        RetryAt = retryAt;
    }

    public DateTimeOffset RetryAt { get; }
}
