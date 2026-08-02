using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Exceptions.ActivityCustomException;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Exceptions.ScrapingCustomException;
using LEB2SCRAPPER.Entity.Exceptions.UserCustomException;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Security;
using System.Net;
using System.Text.Json;

namespace LEB2SCRAPPER.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly TimeProvider _timeProvider;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        TimeProvider timeProvider)
    {
        _next = next;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ILeb2SessionCredential sessionCredential,
        AccessKeyRequestContext accessKeyContext)
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
            var logException = ex is AccessKeyDatabaseException
                ? ex
                : ex.GetBaseException();
            var accessKey = accessKeyContext.Current?.KeyId.ToString();
            var exceptionType = SensitiveDataRedactor.Redact(
                logException.GetType().Name,
                sessionCredential.Value,
                accessKey);
            var message = SensitiveDataRedactor.Redact(
                logException.Message,
                sessionCredential.Value,
                accessKey);
            var traceIdentifier = SensitiveDataRedactor.Redact(
                context.TraceIdentifier,
                sessionCredential.Value,
                accessKey);

            _logger.LogError(
                "Unhandled exception {ExceptionType}: {Message}. Trace identifier: {TraceIdentifier}",
                exceptionType,
                message,
                traceIdentifier);

            await HandleExceptionAsync(context, ex, _timeProvider);
        }
    }

    private static async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        TimeProvider timeProvider)
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
            case AccessKeyRequiredException:
                response.Message = "An access key is required.";
                response.ResponseCode = ApiErrorCodes.AccessKeyRequired;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                break;

            case AccessKeyInvalidException:
                response.Message = "The access key is invalid.";
                response.ResponseCode = ApiErrorCodes.AccessKeyInvalid;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                break;

            case AccessKeyNotActivatedException:
                response.Message = "The access key must be activated through /api/v1/User/login first.";
                response.ResponseCode = ApiErrorCodes.AccessKeyNotActivated;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case AccessKeyAlreadyAssignedException:
                response.Message = "The access key is already assigned to another account.";
                response.ResponseCode = ApiErrorCodes.AccessKeyAlreadyAssigned;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case AccessKeyIdentityMismatchException:
                response.Message = "The access key cannot be used with this account.";
                response.ResponseCode = ApiErrorCodes.AccessKeyIdentityMismatch;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case AccessKeyReauthenticationRequiredException:
                response.Message = "The access key requires reauthentication.";
                response.ResponseCode = ApiErrorCodes.AccessKeyReauthenticationRequired;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case AccessKeyIdentityConflictException:
                response.Message = "The access key identity cannot be registered.";
                response.ResponseCode = ApiErrorCodes.AccessKeyIdentityConflict;
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                break;

            case DeviceIdRequiredException:
                response.Message = "A device ID is required.";
                response.ResponseCode = ApiErrorCodes.DeviceIdRequired;
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                break;

            case DeviceIdInvalidException:
                response.Message = "The device ID is invalid.";
                response.ResponseCode = ApiErrorCodes.DeviceIdInvalid;
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                break;

            case DeviceBindingRequiredException:
                response.Message = "The access key is not bound to this device.";
                response.ResponseCode = ApiErrorCodes.DeviceBindingRequired;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case DeviceBindingMismatchException:
                response.Message = "The access key is bound to another device.";
                response.ResponseCode = ApiErrorCodes.DeviceBindingMismatch;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case AccessKeyDatabaseException databaseException when databaseException.IsTransient:
                response.Message = "Access-key authorization is temporarily unavailable.";
                response.ResponseCode = ApiErrorCodes.AccessKeyStoreUnavailable;
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                break;

            case AccessKeyDatabaseException:
                response.Message = "An internal server error occurred.";
                response.ResponseCode = ApiErrorCodes.UnexpectedError;
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                break;

            case SessionExpiredException:
                response.Message = "The LEB2 session has expired or is invalid.";
                response.ResponseCode = ApiErrorCodes.SessionExpired;
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                break;

            case OutboundRequestBackoffException backoffException:
                var retryAfter = Math.Max(
                    1,
                    (int)Math.Ceiling((
                        backoffException.RetryAt
                        - timeProvider.GetUtcNow()).TotalSeconds));

                response.Message = "LEB2 access is temporarily paused after recent failures.";
                response.ResponseCode = ApiErrorCodes.RequestBackoffActive;
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                context.Response.Headers.RetryAfter = retryAfter.ToString();
                break;

            case OutboundClientThrottleException throttleException:
                var clientRetryAfter = Math.Max(
                    1,
                    (int)Math.Ceiling((
                        throttleException.RetryAt
                        - timeProvider.GetUtcNow()).TotalSeconds));

                response.Message = "This client has too many queued LEB2 requests.";
                response.ResponseCode = ApiErrorCodes.ClientThrottleActive;
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = clientRetryAfter.ToString();
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
