using LEB2SCRAPPER.Entity.Models.Response;
using System.Net;
using System.Text.Json;

namespace LEB2SCRAPPER.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse
        {
            Message = "An error occurred while processing your request.",
            Details = null
        };

        switch (exception)
        {
            case ArgumentException argEx:
                response.Message = "Invalid argument provided.";
                response.Details = argEx.Message;
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;

            case UnauthorizedAccessException:
                response.Message = "Unauthorized access.";
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                break;

            case KeyNotFoundException:
                response.Message = "The requested resource was not found.";
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                break;

            case TimeoutException:
                response.Message = "The request timed out.";
                context.Response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                break;

            case InvalidOperationException invalidOpEx:
                response.Message = "Invalid operation.";
                response.Details = invalidOpEx.Message;
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;

            default:
                response.Message = "An internal server error occurred.";
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                break;
        }

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}
