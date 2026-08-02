using LEB2SCRAPPER.Middleware;

namespace LEB2SCRAPPER.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }

    public static IApplicationBuilder UseLegacyRouteCompatibility(
        this IApplicationBuilder app)
    {
        return app.UseMiddleware<LegacyRouteMiddleware>();
    }

    public static IApplicationBuilder UseClientCompatibility(
        this IApplicationBuilder app)
    {
        return app.UseMiddleware<ClientCompatibilityMiddleware>();
    }
}
