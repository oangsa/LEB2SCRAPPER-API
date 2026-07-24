namespace LEB2SCRAPPER.Infrastructure.HttpService;

public static class Leb2HttpClientHandlerFactory
{
    public static HttpClientHandler Create()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };
    }
}
