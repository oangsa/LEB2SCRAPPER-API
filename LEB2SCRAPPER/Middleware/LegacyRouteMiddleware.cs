using LEB2SCRAPPER.Configuration;

namespace LEB2SCRAPPER.Middleware;

public sealed class LegacyRouteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiVersioningOptions _options;

    public LegacyRouteMiddleware(
        RequestDelegate next,
        ApiVersioningOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.LegacyRoutesEnabled
            && IsLegacyRoute(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }

    private static bool IsLegacyRoute(PathString path)
    {
        var value = path.Value?.TrimEnd('/');

        return string.Equals(value, "/User/login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/User/cookie", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/User/logout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/Semester", StringComparison.OrdinalIgnoreCase)
            || value?.StartsWith("/Class/", StringComparison.OrdinalIgnoreCase) == true
            || value?.StartsWith("/Activity/", StringComparison.OrdinalIgnoreCase) == true
            || string.Equals(value, "/health/leb2", StringComparison.OrdinalIgnoreCase);
    }
}
