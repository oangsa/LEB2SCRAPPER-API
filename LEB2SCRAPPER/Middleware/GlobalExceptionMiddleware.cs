using LEB2SCRAPPER.Entity.Exceptions.ActivityCustomException;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Exceptions.ScrapingCustomException;
using LEB2SCRAPPER.Entity.Exceptions.UserCustomException;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using LEB2SCRAPPER.Security;
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

    public async Task InvokeAsync(
        HttpContext context,
        ILeb2SessionCredential sessionCredential)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Request {TraceIdentifier} was canceled by the client.",
                context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            var baseException = ex.GetBaseException();
            var exceptionType = SensitiveDataRedactor.Redact(
                baseException.GetType().Name,
                sessionCredential.Value);
            var message = SensitiveDataRedactor.Redact(
                baseException.Message,
                sessionCredential.Value);
            var traceIdentifier = SensitiveDataRedactor.Redact(
                context.TraceIdentifier,
                sessionCredential.Value);

            _logger.LogError(
                "Unhandled exception {ExceptionType}: {Message}. Trace identifier: {TraceIdentifier}",
                exceptionType,
                message,
                traceIdentifier);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse
        {
            Message = "An error occurred while processing your request.",
            ResponseCode = ApiErrorCodes.UnexpectedError,
            Details = null
        };

        switch (exception)
        {
            case SessionExpiredException:
                response.Message = "The LEB2 session has expired or is invalid.";
                response.ResponseCode = ApiErrorCodes.SessionExpired;
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                break;

            case OutboundRequestBackoffException backoffException:
                var retryAfter = Math.Max(
                    1,
                    (int)Math.Ceiling((backoffException.RetryAt - DateTimeOffset.UtcNow).TotalSeconds));

                response.Message = "LEB2 access is temporarily paused after recent failures.";
                response.ResponseCode = ApiErrorCodes.RequestBackoffActive;
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                context.Response.Headers.RetryAfter = retryAfter.ToString();
                break;

            case StructuralParseException:
                response.Message = "LEB2 returned an unexpected response structure.";
                response.ResponseCode = ApiErrorCodes.ScrapeResponseChanged;
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                break;

            case TransientLeb2Exception:
                response.Message = "LEB2 is temporarily unavailable.";
                response.ResponseCode = ApiErrorCodes.Leb2Unavailable;
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                break;

            case Leb2UpstreamException:
            case ScrapingCustomException:
            case ActivityCustomExceptionException:
                response.Message = "The LEB2 request could not be completed.";
                response.ResponseCode = ApiErrorCodes.Leb2Unavailable;
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                break;

            case ArgumentException:
                response.Message = "Invalid argument provided.";
                response.ResponseCode = ApiErrorCodes.InvalidRequest;
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;

            case UnauthorizedAccessException:
                response.Message = "Unauthorized access.";
                response.ResponseCode = ApiErrorCodes.AuthenticationRequired;
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                break;

            case KeyNotFoundException:
            case UserNotFoundException:
                response.Message = "The requested resource was not found.";
                response.ResponseCode = ApiErrorCodes.ResourceNotFound;
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                break;

            case TimeoutException:
                response.Message = "The request timed out.";
                response.ResponseCode = ApiErrorCodes.Leb2Unavailable;
                context.Response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                break;

            case InvalidOperationException:
                response.Message = "Invalid operation.";
                response.ResponseCode = ApiErrorCodes.InvalidRequest;
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;

            default:
                response.Message = "An internal server error occurred.";
                response.ResponseCode = ApiErrorCodes.UnexpectedError;
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                break;
        }

        response.TraceId = context.TraceIdentifier;

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}
