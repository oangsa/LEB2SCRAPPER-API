using System.Text.Json;
using LEB2SCRAPPER.Configuration;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;
using Microsoft.AspNetCore.Authorization;

namespace LEB2SCRAPPER.Middleware;

public sealed class ClientCompatibilityMiddleware
{
    public const string ClientVersionHeaderName =
        ClientCompatibilityOptions.ClientVersionHeaderName;

    private readonly RequestDelegate _next;
    private readonly ClientCompatibilityConfiguration _configuration;

    public ClientCompatibilityMiddleware(
        RequestDelegate next,
        ClientCompatibilityConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_configuration.EnforcementEnabled
            || context.GetEndpoint() is null
            || context.GetEndpoint()!.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        var values = context.Request.Headers[ClientVersionHeaderName];

        if (values.Count == 0
            || (values.Count == 1 && string.IsNullOrWhiteSpace(values[0])))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ClientVersionRequired,
                "A client version is required.");
            return;
        }

        if (values.Count != 1
            || !SemanticVersion.TryParse(values[0]!, out var clientVersion))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ClientVersionInvalid,
                "The client version is invalid.");
            return;
        }

        if (clientVersion < _configuration.MinimumVersion)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status426UpgradeRequired,
                ApiErrorCodes.ClientUpdateRequired,
                $"This client version is no longer supported. Update to {_configuration.MinimumClientVersion}.");
            return;
        }

        await _next(context);
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string responseCode,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var response = new ErrorResponse
        {
            Message = message,
            ResponseCode = responseCode,
            TraceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }
}
