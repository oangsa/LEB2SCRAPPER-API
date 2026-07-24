using System.Text.Json;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Exceptions.UserCustomException;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using LEB2SCRAPPER.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LEB2SCRAPPER.Tests.Middleware;

public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task SessionExpired_ReturnsMachineReadableUnauthorizedResponse()
    {
        var (context, response) = await InvokeAsync(new SessionExpiredException());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(ApiErrorCodes.SessionExpired, response.ResponseCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate);
    }

    [Fact]
    public async Task TransientFailure_ReturnsDistinctUnavailableResponse()
    {
        var (context, response) = await InvokeAsync(
            new TransientLeb2Exception("LEB2 is temporarily unavailable."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal(ApiErrorCodes.Leb2Unavailable, response.ResponseCode);
    }

    [Fact]
    public async Task WrappedTransientFailure_LogsRedactedBaseCauseAndRetainsUnavailableResponse()
    {
        const string sessionValue = "fake-leb2-session-value";
        const string traceIdentifier = "test-trace-id";
        var logger = new CapturingLogger();
        var exception = new TransientLeb2Exception(
            $"The browser failed with Cookie: {sessionValue}",
            new InvalidOperationException(
                $"ChromeDriver rejected Cookie: {sessionValue}"));

        var (context, response) = await InvokeAsync(
            exception,
            logger,
            new StaticSessionCredential(sessionValue),
            traceIdentifier);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal(ApiErrorCodes.Leb2Unavailable, response.ResponseCode);
        Assert.Null(logger.Exception);

        var message = Assert.IsType<string>(logger.Message);
        Assert.Contains(nameof(InvalidOperationException), message);
        Assert.Contains(traceIdentifier, message);
        Assert.Contains("[REDACTED]", message);
        Assert.DoesNotContain(nameof(TransientLeb2Exception), message);
        Assert.DoesNotContain(sessionValue, message);
    }

    [Fact]
    public async Task UserNotFound_ReturnsMachineReadableNotFoundResponse()
    {
        var (context, response) = await InvokeAsync(
            new UserNotFoundException("Invalid credentials."));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(ApiErrorCodes.ResourceNotFound, response.ResponseCode);
    }

    [Fact]
    public async Task ClientCancellation_DoesNotWriteAnErrorResponse()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new OperationCanceledException(cancellationSource.Token),
            NullLogger<GlobalExceptionMiddleware>.Instance,
            TimeProvider.System);
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellationSource.Token
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(
            context,
            new StaticSessionCredential(null));

        Assert.Equal(0, context.Response.Body.Length);
    }

    private static async Task<(DefaultHttpContext Context, ErrorResponse Response)> InvokeAsync(
        Exception exception,
        ILogger<GlobalExceptionMiddleware>? logger = null,
        ILeb2SessionCredential? sessionCredential = null,
        string? traceIdentifier = null)
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw exception,
            logger ?? NullLogger<GlobalExceptionMiddleware>.Instance,
            TimeProvider.System);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (traceIdentifier is not null)
        {
            context.TraceIdentifier = traceIdentifier;
        }

        await middleware.InvokeAsync(
            context,
            sessionCredential ?? new StaticSessionCredential(null));

        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return (context, Assert.IsType<ErrorResponse>(response));
    }

    private sealed class StaticSessionCredential : ILeb2SessionCredential
    {
        public StaticSessionCredential(string? value)
        {
            Value = value;
        }

        public string? Value { get; }
    }

    private sealed class CapturingLogger : ILogger<GlobalExceptionMiddleware>
    {
        public Exception? Exception { get; private set; }
        public string? Message { get; private set; }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoopDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exception = exception;
            Message = formatter(state, exception);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
