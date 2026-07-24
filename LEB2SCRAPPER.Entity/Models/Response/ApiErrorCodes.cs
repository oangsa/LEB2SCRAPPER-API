namespace LEB2SCRAPPER.Entity.Models.Response;

public static class ApiErrorCodes
{
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string Leb2Unavailable = "LEB2_UNAVAILABLE";
    public const string RequestBackoffActive = "REQUEST_BACKOFF_ACTIVE";
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    public const string ScrapeResponseChanged = "SCRAPE_RESPONSE_CHANGED";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string UnexpectedError = "UNEXPECTED_ERROR";
}
