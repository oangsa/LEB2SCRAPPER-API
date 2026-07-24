using System.Net;
using System.Text;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Infrastructure.HttpService;

namespace LEB2SCRAPPER.Tests.Infrastructure;

public class HttpServiceTests
{
    [Fact]
    public async Task GetAsync_WithValidJson_ReturnsData()
    {
        using var httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":42}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = new HttpService(httpClient, new PassthroughOutboundRequestGate());

        var result = await service.GetAsync<TestResponse>(
            "https://app.leb2.org/api/test",
            new OutboundRequestContext("test", "client"));

        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_WithExpiredSessionRedirect_ThrowsSessionExpired()
    {
        using var httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers =
                {
                    Location = new Uri("https://www.leb2.org/")
                }
            });
        var service = new HttpService(httpClient, new PassthroughOutboundRequestGate());

        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            service.GetAsync<TestResponse>(
                "https://app.leb2.org/api/test",
                new OutboundRequestContext(
                    "test",
                    "client",
                    UsesSessionCredential: true)));
    }

    [Fact]
    public async Task GetAsync_WithServerFailure_ThrowsTransientFailure()
    {
        using var httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = new HttpService(httpClient, new PassthroughOutboundRequestGate());

        await Assert.ThrowsAsync<TransientLeb2Exception>(() =>
            service.GetAsync<TestResponse>(
                "https://app.leb2.org/api/test",
                new OutboundRequestContext("test", "client")));
    }

    [Fact]
    public async Task GetAsync_WithMissingRequiredJsonShape_ThrowsStructuralFailure()
    {
        using var httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = new HttpService(httpClient, new PassthroughOutboundRequestGate());

        await Assert.ThrowsAsync<StructuralParseException>(() =>
            service.GetAsync<ActivityResponse>(
                "https://app.leb2.org/api/test",
                new OutboundRequestContext("activities", "client")));
    }

    [Fact]
    public async Task GetAsync_WithMissingLoginSuccessField_ThrowsStructuralFailure()
    {
        using var httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = new HttpService(httpClient, new PassthroughOutboundRequestGate());

        await Assert.ThrowsAsync<StructuralParseException>(() =>
            service.GetAsync<LoginResponse>(
                "https://leb2-mcs-api-production.leb2.org/public/login/v1/login",
                new OutboundRequestContext("user-login", "client")));
    }

    [Fact]
    public async Task PostAsync_WithMixedCaseLoginJson_MapsLoginResult()
    {
        using var httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "success": true,
                        "result": {
                            "studentId": "fake-student-id",
                            "radiusExpiration": "fake-expiration",
                            "id": 123
                        },
                        "rememberToken": "fake-remember-token"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        var service = new HttpService(httpClient, new PassthroughOutboundRequestGate());

        var result = await service.PostAsync<LoginResponse>(
            "https://leb2-mcs-api-production.leb2.org/public/login/v1/login",
            body: null,
            new OutboundRequestContext("user-login", "client"));

        Assert.Equal("fake-student-id", result.Result?.StudentId);
        Assert.Equal("fake-expiration", result.Result?.RadiusExpiration);
        Assert.Equal("fake-remember-token", result.RememberToken);
    }

    [Fact]
    public async Task GetAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var httpClient = new HttpClient(new CanceledHttpMessageHandler());
        var service = new HttpService(httpClient, new PassthroughOutboundRequestGate());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetAsync<TestResponse>(
                "https://app.leb2.org/api/test",
                new OutboundRequestContext("test", "client"),
                cancellationToken: cancellationSource.Token));
    }

    [Fact]
    public void Leb2HttpClientHandler_DisablesAutomaticCookieStorage()
    {
        using var handler = Leb2HttpClientHandlerFactory.Create();

        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
    }

    private static HttpClient CreateHttpClient(HttpResponseMessage response)
    {
        return new HttpClient(new StubHttpMessageHandler(response));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class CanceledHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The canceled request unexpectedly completed.");
        }
    }

    private sealed class PassthroughOutboundRequestGate : IOutboundRequestGate
    {
        public Task<T> ExecuteAsync<T>(
            OutboundRequestContext context,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return operation(cancellationToken);
        }
    }

    private sealed class TestResponse
    {
        public int Value { get; set; }
    }
}
