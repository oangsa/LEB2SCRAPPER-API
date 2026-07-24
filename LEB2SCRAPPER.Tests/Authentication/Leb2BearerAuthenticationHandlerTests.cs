using System.Text.Encodings.Web;
using LEB2SCRAPPER.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LEB2SCRAPPER.Tests.Authentication;

public class Leb2BearerAuthenticationHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_StoresOpaqueBearerOnlyForRequestLifetime()
    {
        const string sessionCookie = "leb2_session=fake-session; other=fake-value";
        var credential = new Leb2SessionCredential();
        var handler = new Leb2BearerAuthenticationHandler(
            new TestOptionsMonitor<AuthenticationSchemeOptions>(
                new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            credential);
        var responseFeature = new CompletingHttpResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Request.Headers.Authorization = $"Bearer {sessionCookie}";
        await handler.InitializeAsync(
            new AuthenticationScheme(
                Leb2BearerDefaults.AuthenticationScheme,
                Leb2BearerDefaults.AuthenticationScheme,
                typeof(Leb2BearerAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(sessionCookie, credential.Value);
        Assert.DoesNotContain(
            result.Principal!.Claims,
            claim => claim.Value.Contains("fake-session", StringComparison.Ordinal));

        await responseFeature.CompleteAsync();

        Assert.Null(credential.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_AcceptsLegacyRawAuthorizationValue()
    {
        const string sessionCookie = "leb2_session=fake-legacy-session";
        var credential = new Leb2SessionCredential();
        var handler = new Leb2BearerAuthenticationHandler(
            new TestOptionsMonitor<AuthenticationSchemeOptions>(
                new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            credential);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = sessionCookie;
        await handler.InitializeAsync(
            new AuthenticationScheme(
                Leb2BearerDefaults.AuthenticationScheme,
                Leb2BearerDefaults.AuthenticationScheme,
                typeof(Leb2BearerAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(sessionCookie, credential.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsBearerWithoutCredential()
    {
        var credential = new Leb2SessionCredential();
        var handler = new Leb2BearerAuthenticationHandler(
            new TestOptionsMonitor<AuthenticationSchemeOptions>(
                new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            credential);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer";
        await handler.InitializeAsync(
            new AuthenticationScheme(
                Leb2BearerDefaults.AuthenticationScheme,
                Leb2BearerDefaults.AuthenticationScheme,
                typeof(Leb2BearerAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(credential.Value);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }

    private sealed class CompletingHttpResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = new();

        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            _callbacks.Add((callback, state));
        }

        public async Task CompleteAsync()
        {
            foreach (var (callback, state) in _callbacks.AsEnumerable().Reverse())
            {
                await callback(state);
            }
        }
    }
}
