using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Service.Contracts.Master;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LEB2SCRAPPER.Tests.Integration;

public class ApiIntegrationTests
{
    private const string UserIdHeaderName = "X-LEB2-USER-ID";
    private static readonly Guid FakeAccessKeyId =
        Guid.Parse("9a7b979b-a361-4170-aee7-cba89445495b");

    [Fact]
    public async Task ClassActivityRoute_PropagatesInputsAndReturnsFlatActivities()
    {
        var activityService = new FakeActivityService
        {
            GetByClassHandler = (userId, semesterId, classId, token, _) =>
            {
                Assert.Equal(123, userId);
                Assert.Equal(10, semesterId);
                Assert.Equal(20, classId);
                Assert.Equal("fake-session", token);

                return Task.FromResult<List<Activity>?>(
                [
                    new Activity { Id = 1, ClassId = classId },
                    new Activity { Id = 2, ClassId = classId }
                ]);
            }
        };
        using var factory = CreateFactory(activityService);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Activity/10/20");
        var activities = await response.Content.ReadFromJsonAsync<List<Activity>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1, 2], activities!.Select(activity => activity.Id));
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, ApiErrorCodes.ResourceNotFound)]
    [InlineData("session", HttpStatusCode.Unauthorized, ApiErrorCodes.SessionExpired)]
    [InlineData("structural", HttpStatusCode.BadGateway, ApiErrorCodes.ScrapeResponseChanged)]
    [InlineData("transient", HttpStatusCode.ServiceUnavailable, ApiErrorCodes.Leb2Unavailable)]
    public async Task ClassActivityRoute_MembershipFailuresUseMiddlewareContract(
        string failureKind,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var activityService = new FakeActivityService
        {
            GetByClassHandler = (_, _, _, _, _) => failureKind switch
            {
                "not-found" => throw new KeyNotFoundException(
                    "The requested class is not in the semester."),
                "session" => throw new SessionExpiredException(),
                "structural" => throw new StructuralParseException(
                    "classes.class_cards",
                    "Synthetic structural failure."),
                _ => throw new TransientLeb2Exception(
                    "Synthetic transient failure.")
            }
        };
        using var factory = CreateFactory(activityService);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Activity/10/20");
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, error?.ResponseCode);
    }

    [Fact]
    public async Task SemesterActivityRoute_PropagatesInputsAndReturnsFlatActivities()
    {
        var activityService = new FakeActivityService
        {
            GetBySemesterHandler = (userId, semesterId, token, _) =>
            {
                Assert.Equal(123, userId);
                Assert.Equal(10, semesterId);
                Assert.Equal("fake-session", token);

                return Task.FromResult(new List<Activity>
                {
                    new() { Id = 1, ClassId = 20 },
                    new() { Id = 2, ClassId = 30 }
                });
            }
        };
        using var factory = CreateFactory(activityService);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Activity/10");
        var activities = await response.Content.ReadFromJsonAsync<List<Activity>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([20, 30], activities!.Select(activity => activity.ClassId));
    }

    [Fact]
    public async Task SemesterSnapshotRoute_PropagatesInputsAndReturnsNestedShape()
    {
        var activityService = new FakeActivityService
        {
            GetSnapshotHandler = (userId, semesterId, token, _) =>
            {
                Assert.Equal(123, userId);
                Assert.Equal(10, semesterId);
                Assert.Equal("fake-session", token);

                return Task.FromResult(new SemesterSnapshotResponse
                {
                    SemesterId = semesterId,
                    Classes =
                    [
                        new SemesterSnapshotClass
                        {
                            Id = 20,
                            Name = "Example Class",
                            Activities =
                            [
                                new Activity { Id = 1, ClassId = 20 }
                            ]
                        },
                        new SemesterSnapshotClass
                        {
                            Id = 30,
                            Name = "Empty Class"
                        }
                    ]
                });
            }
        };
        using var factory = CreateFactory(activityService);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Activity/10/snapshot");
        using var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = content!.RootElement;
        var classes = root.GetProperty("classes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, root.GetProperty("semesterId").GetInt32());
        Assert.Equal(2, classes.GetArrayLength());
        Assert.Equal(20, classes[0].GetProperty("id").GetInt32());
        Assert.Equal("Example Class", classes[0].GetProperty("name").GetString());
        Assert.Equal(1, classes[0].GetProperty("activities").GetArrayLength());
        Assert.Equal(0, classes[1].GetProperty("activities").GetArrayLength());
    }

    [Theory]
    [InlineData("/Activity/10")]
    [InlineData("/Activity/10/20")]
    [InlineData("/Activity/10/snapshot")]
    public async Task ActivityRoutes_RequireAuthentication(string path)
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserIdHeaderName, "123");

        var response = await client.GetAsync(path);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AuthenticationRequired, error?.ResponseCode);
    }

    [Theory]
    [InlineData("/Semester")]
    [InlineData("/Class/10")]
    [InlineData("/Activity/10")]
    [InlineData("/Activity/10/20")]
    [InlineData("/Activity/10/snapshot")]
    public async Task Leb2Routes_RequireAccessKey(string path)
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "fake-session");
        client.DefaultRequestHeaders.Add(UserIdHeaderName, "123");

        var response = await client.GetAsync(path);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AccessKeyRequired, error?.ResponseCode);
    }

    [Fact]
    public async Task ProtectedRoute_WithAssignedAccessKeyAndMissingLeb2Session_UsesExistingAuthFailure()
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/Semester");
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AuthenticationRequired, error?.ResponseCode);
    }

    [Fact]
    public async Task ProtectedRoute_WithUnassignedAccessKeyIsRejectedBeforeService()
    {
        using var factory = CreateFactory(
            new FakeActivityService(),
            accessKeyService: new FakeAccessKeyService(assigned: false));
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Semester");
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AccessKeyNotActivated, error?.ResponseCode);
    }

    [Fact]
    public async Task Login_RequiresProvisionedAccessKeyBeforeCallingUserService()
    {
        var userService = new FakeUserService
        {
            LoginHandler = (_, _, _) =>
                throw new InvalidOperationException("User service should not run.")
        };
        using var factory = CreateFactory(
            new FakeActivityService(),
            userService: userService);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/User/login",
            new Credentials
            {
                Username = "fake-student",
                Password = "fake-password"
            });
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AccessKeyRequired, error?.ResponseCode);
    }

    [Fact]
    public async Task Login_WithProvisionedAccessKeyPreservesSuccessfulResponse()
    {
        var accessKeyId = FakeAccessKeyId;
        var userService = new FakeUserService
        {
            LoginHandler = (credentials, keyId, _) =>
            {
                Assert.Equal("fake-student", credentials.Username);
                Assert.Equal(accessKeyId, keyId);
                return Task.FromResult<User?>(new User
                {
                    Id = 42,
                    KmuttId = "60000000",
                    NameThai = "ชื่อ",
                    NameEnglish = "Example",
                    SurnameThai = "นามสกุล",
                    SurnameEnglish = "Student"
                });
            }
        };
        using var factory = CreateFactory(
            new FakeActivityService(),
            userService: userService);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            AccessKeyAuthorizationFilter.HeaderName,
            accessKeyId.ToString());

        var response = await client.PostAsJsonAsync(
            "/User/login",
            new Credentials
            {
                Username = "fake-student",
                Password = "fake-password"
            });
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(42, body!.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(
            "60000000",
            body.RootElement.GetProperty("kmuttId").GetString());
    }

    [Fact]
    public async Task Cookie_WithUnassignedAccessKeyIsRejectedBeforeSelenium()
    {
        var userService = new FakeUserService
        {
            CookieHandler = (_, _) =>
                throw new InvalidOperationException("Cookie service should not run.")
        };
        using var factory = CreateFactory(
            new FakeActivityService(),
            accessKeyService: new FakeAccessKeyService(assigned: false),
            userService: userService);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            AccessKeyAuthorizationFilter.HeaderName,
            FakeAccessKeyId.ToString());

        var response = await client.PostAsJsonAsync(
            "/User/cookie",
            new Credentials
            {
                Username = "fake-student",
                Password = "fake-password"
            });
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AccessKeyNotActivated, error?.ResponseCode);
    }

    [Fact]
    public async Task Cookie_WithAssignedAccessKeyRunsExistingCookieFlow()
    {
        var userService = new FakeUserService
        {
            CookieHandler = (credentials, _) =>
            {
                Assert.Equal("fake-student", credentials.Username);
                return Task.FromResult<string?>("fake-cookie");
            }
        };
        using var factory = CreateFactory(
            new FakeActivityService(),
            userService: userService);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            AccessKeyAuthorizationFilter.HeaderName,
            FakeAccessKeyId.ToString());

        var response = await client.PostAsJsonAsync(
            "/User/cookie",
            new Credentials
            {
                Username = "fake-student",
                Password = "fake-password"
            });
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("fake-cookie", body!.RootElement.GetProperty("cookie").GetString());
    }

    [Theory]
    [InlineData("/Activity/10", null)]
    [InlineData("/Activity/10", "0")]
    [InlineData("/Activity/10", "not-an-integer")]
    [InlineData("/Activity/10/20", null)]
    [InlineData("/Activity/10/20", "-1")]
    [InlineData("/Activity/10/snapshot", null)]
    [InlineData("/Activity/10/snapshot", "0")]
    public async Task ActivityRoutes_RequirePositiveIntegerUserHeader(
        string path,
        string? userIdHeader)
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = CreateAuthenticatedClient(factory, userId: null);

        if (userIdHeader is not null)
        {
            client.DefaultRequestHeaders.Add(UserIdHeaderName, userIdHeader);
        }

        var response = await client.GetAsync(path);
        var error = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.InvalidRequest, error?.ResponseCode);
    }

    [Theory]
    [InlineData("/Activity/0")]
    [InlineData("/Activity/-1")]
    [InlineData("/Activity/0/20")]
    [InlineData("/Activity/10/0")]
    [InlineData("/Activity/10/-1")]
    [InlineData("/Activity/0/snapshot")]
    [InlineData("/Activity/-1/snapshot")]
    public async Task ActivityRoutes_RequirePositiveIntegerRouteValues(string path)
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync(path);
        var error = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.InvalidRequest, error?.ResponseCode);
    }

    [Theory]
    [InlineData("/Activity/not-an-integer")]
    [InlineData("/Activity/10/not-an-integer")]
    [InlineData("/Activity/not-an-integer/snapshot")]
    public async Task ActivityRoutes_WithNonIntegerRouteValues_DoNotMatch(string path)
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BareActivityRoute_DoesNotExist()
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Activity");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("structural", HttpStatusCode.BadGateway, ApiErrorCodes.ScrapeResponseChanged)]
    [InlineData("transient", HttpStatusCode.ServiceUnavailable, ApiErrorCodes.Leb2Unavailable)]
    [InlineData("throttle", HttpStatusCode.TooManyRequests, ApiErrorCodes.ClientThrottleActive)]
    public async Task ActivityFailuresUseMiddlewareContract(
        string failureKind,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var activityService = new FakeActivityService
        {
            GetBySemesterHandler = (_, _, _, _) => failureKind switch
            {
                "structural" => throw new StructuralParseException(
                    "classes.class_cards",
                    "Synthetic structural failure."),
                "transient" => throw new TransientLeb2Exception(
                    "Synthetic transient failure."),
                _ => throw new OutboundClientThrottleException(
                    TimeProvider.System.GetUtcNow().AddSeconds(1))
            }
        };
        using var factory = CreateFactory(activityService);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/Activity/10");
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, error?.ResponseCode);

        if (expectedStatus == HttpStatusCode.TooManyRequests)
        {
            Assert.NotNull(response.Headers.RetryAfter);
        }
    }

    [Fact]
    public async Task HealthEndpoint_IsAnonymousDegradedAndNeverCached()
    {
        var observedAt = new DateTimeOffset(
            2026,
            7,
            24,
            0,
            0,
            0,
            TimeSpan.Zero);
        var retryAt = observedAt.AddSeconds(30);
        var snapshot = new OutboundRequestStatusSnapshot(
            observedAt,
            Leb2OutboundEndpoints.All
                .Select(endpoint => endpoint == Leb2OutboundEndpoints.Activities
                    ? new OutboundEndpointStatus(endpoint, retryAt, 30)
                    : new OutboundEndpointStatus(endpoint, null, 0))
                .ToList());
        using var factory = CreateFactory(
            new FakeActivityService(),
            new StaticStatusReader(snapshot));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/leb2");
        var health = await response.Content.ReadFromJsonAsync<Leb2HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("degraded", health?.Status);
        Assert.Equal("local-observed-state", health?.Source);
        Assert.Equal(Leb2OutboundEndpoints.All, health?.Endpoints.Select(e => e.Name));
        var activities = Assert.Single(
            health!.Endpoints,
            endpoint => endpoint.Name == Leb2OutboundEndpoints.Activities);
        Assert.Equal("unavailable", activities.Status);
        Assert.Equal(30, activities.RetryAfterSeconds);
    }

    [Fact]
    public async Task HealthEndpoint_IsHealthyWhenNoEndpointHasActiveBackoff()
    {
        var observedAt = new DateTimeOffset(
            2026,
            7,
            24,
            0,
            0,
            0,
            TimeSpan.Zero);
        var snapshot = new OutboundRequestStatusSnapshot(
            observedAt,
            Leb2OutboundEndpoints.All
                .Select(endpoint => new OutboundEndpointStatus(endpoint, null, 0))
                .ToList());
        using var factory = CreateFactory(
            new FakeActivityService(),
            new StaticStatusReader(snapshot));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/leb2");
        var health = await response.Content.ReadFromJsonAsync<Leb2HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("healthy", health?.Status);
        Assert.Equal("local-observed-state", health?.Source);
        Assert.All(health!.Endpoints, endpoint =>
        {
            Assert.Equal("available", endpoint.Status);
            Assert.Null(endpoint.RetryAt);
            Assert.Equal(0, endpoint.RetryAfterSeconds);
        });
    }

    [Fact]
    public async Task Swagger_AdvertisesActivityGetRoutesAndHealthContract()
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = factory.CreateClient();

        using var swagger = await client.GetFromJsonAsync<JsonDocument>(
            "/swagger/v1/swagger.json");
        var paths = swagger!.RootElement.GetProperty("paths");
        var classPath = paths.GetProperty("/Activity/{semesterId}/{classId}");
        var semesterPath = paths.GetProperty("/Activity/{semesterId}");
        var snapshotPath = paths.GetProperty("/Activity/{semesterId}/snapshot");

        AssertActivityOperation(classPath.GetProperty("get"));
        Assert.Contains(
            "404",
            classPath
                .GetProperty("get")
                .GetProperty("responses")
                .EnumerateObject()
                .Select(property => property.Name));
        AssertActivityOperation(semesterPath.GetProperty("get"));
        AssertActivityOperation(snapshotPath.GetProperty("get"));
        Assert.False(classPath.TryGetProperty("post", out _));
        Assert.False(semesterPath.TryGetProperty("post", out _));
        Assert.False(paths.TryGetProperty("/Activity", out _));
        Assert.False(paths.TryGetProperty("/Activity/all", out _));
        Assert.True(paths.GetProperty("/health/leb2").TryGetProperty("get", out _));
    }

    [Fact]
    public async Task Swagger_AdvertisesSeparateAccessKeyAndLeb2Requirements()
    {
        using var factory = CreateFactory(new FakeActivityService());
        using var client = factory.CreateClient();

        using var swagger = await client.GetFromJsonAsync<JsonDocument>(
            "/swagger/v1/swagger.json");
        var root = swagger!.RootElement;
        var paths = root.GetProperty("paths");
        var loginSecurity = paths
            .GetProperty("/User/login")
            .GetProperty("post")
            .GetProperty("security")[0];
        var cookieSecurity = paths
            .GetProperty("/User/cookie")
            .GetProperty("post")
            .GetProperty("security")[0];
        var activitySecurity = paths
            .GetProperty("/Activity/{semesterId}")
            .GetProperty("get")
            .GetProperty("security")[0];
        var schemes = root
            .GetProperty("components")
            .GetProperty("securitySchemes");

        Assert.True(loginSecurity.TryGetProperty("AccessKey", out _));
        Assert.False(loginSecurity.TryGetProperty("Leb2Bearer", out _));
        Assert.True(cookieSecurity.TryGetProperty("AccessKey", out _));
        Assert.False(cookieSecurity.TryGetProperty("Leb2Bearer", out _));
        Assert.True(activitySecurity.TryGetProperty("AccessKey", out _));
        Assert.True(activitySecurity.TryGetProperty("Leb2Bearer", out _));
        Assert.Equal(
            "apiKey",
            schemes.GetProperty("AccessKey").GetProperty("type").GetString());
        Assert.Equal(
            "access-key",
            schemes.GetProperty("AccessKey").GetProperty("name").GetString());
        Assert.Equal(
            "header",
            schemes.GetProperty("AccessKey").GetProperty("in").GetString());
    }

    private static void AssertActivityOperation(JsonElement operation)
    {
        var responseCodes = operation
            .GetProperty("responses")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet();

        Assert.All(
            new[]
            {
                "200",
                "400",
                "401",
                "403",
                "429",
                "500",
                "502",
                "503"
            },
            responseCode => Assert.Contains(responseCode, responseCodes));
        Assert.True(operation.TryGetProperty("security", out _));

        var parameters = operation.GetProperty("parameters");
        var userIdHeader = Assert.Single(
            parameters.EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == UserIdHeaderName);

        Assert.Equal("header", userIdHeader.GetProperty("in").GetString());
        Assert.True(userIdHeader.GetProperty("required").GetBoolean());
        Assert.Equal(
            "integer",
            userIdHeader.GetProperty("schema").GetProperty("type").GetString());

        Assert.All(
            parameters
                .EnumerateArray()
                .Where(parameter => parameter.GetProperty("in").GetString() == "path"),
            parameter => Assert.Equal(
                "integer",
                parameter.GetProperty("schema").GetProperty("type").GetString()));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IActivityService activityService,
        IOutboundRequestStatusReader? statusReader = null,
        IAccessKeyService? accessKeyService = null,
        IUserService? userService = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                {
                    var resolvedAccessKeyService =
                        accessKeyService ?? new FakeAccessKeyService();

                    services.RemoveAll<IServiceManager>();
                    services.AddSingleton<IServiceManager>(
                        new FakeServiceManager(
                            activityService,
                            resolvedAccessKeyService,
                            userService));
                    services.RemoveAll<IAccessKeyService>();
                    services.AddSingleton<IAccessKeyService>(resolvedAccessKeyService);

                    if (statusReader is not null)
                    {
                        services.RemoveAll<IOutboundRequestStatusReader>();
                        services.AddSingleton(statusReader);
                    }
                });
            });
    }

    private static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> factory,
        string? userId = "123")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            AccessKeyAuthorizationFilter.HeaderName,
            FakeAccessKeyId.ToString());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "fake-session");

        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(UserIdHeaderName, userId);
        }

        return client;
    }

    private sealed class StaticStatusReader : IOutboundRequestStatusReader
    {
        private readonly OutboundRequestStatusSnapshot _snapshot;

        public StaticStatusReader(OutboundRequestStatusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public OutboundRequestStatusSnapshot GetSnapshot()
        {
            return _snapshot;
        }
    }

    private sealed class FakeServiceManager : IServiceManager
    {
        public FakeServiceManager(
            IActivityService activityService,
            IAccessKeyService accessKeyService,
            IUserService? userService)
        {
            ActivityService = activityService;
            AccessKeyService = accessKeyService;
            UserService = userService ?? new UnsupportedUserService();
        }

        public IActivityService ActivityService { get; }

        public IAccessKeyService AccessKeyService { get; }

        public IUserService UserService { get; }

        public IClassService ClassService { get; } = new UnsupportedClassService();

        public ISemesterService SemesterService { get; } =
            new UnsupportedSemesterService();
    }

    private sealed class FakeAccessKeyService : IAccessKeyService
    {
        private readonly bool _assigned;

        public FakeAccessKeyService(bool assigned = true)
        {
            _assigned = assigned;
        }

        public Task<AccessKeyState> ValidateProvisionedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AccessKeyState(
                keyId,
                _assigned
                    ? Guid.Parse("d2ac4cb8-53f5-4cc9-ae8c-4ec5fb1c9d7e")
                    : null,
                _assigned ? "fake-student" : null));
        }

        public Task<AccessKeyState> ValidateActivatedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            if (!_assigned)
            {
                throw new AccessKeyNotActivatedException();
            }

            return ValidateProvisionedKeyAsync(keyId, cancellationToken);
        }

        public Task RegisterSuccessfulLoginAsync(
            Guid keyId,
            string studentId,
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserService : IUserService
    {
        public Func<Credentials, Guid, CancellationToken, Task<User?>> LoginHandler { get; set; } =
            (_, _, _) => Task.FromResult<User?>(new User
            {
                Id = 42,
                KmuttId = "60000000",
                NameEnglish = "Example",
                SurnameEnglish = "Student"
            });

        public Func<Credentials, CancellationToken, Task<string?>> CookieHandler { get; set; } =
            (_, _) => Task.FromResult<string?>("fake-cookie");

        public Task<User?> GetUserByCredentialsAsync(
            Credentials credentials,
            Guid accessKeyId,
            CancellationToken cancellationToken = default)
        {
            return LoginHandler(credentials, accessKeyId, cancellationToken);
        }

        public Task<string?> GetCookieAsync(
            Credentials credentials,
            CancellationToken cancellationToken = default)
        {
            return CookieHandler(credentials, cancellationToken);
        }
    }

    private sealed class FakeActivityService : IActivityService
    {
        public Func<
            int,
            int,
            int,
            string,
            CancellationToken,
            Task<List<Activity>?>> GetByClassHandler { get; set; } =
                (_, _, _, _, _) => Task.FromResult<List<Activity>?>([]);

        public Func<
            int,
            int,
            string,
            CancellationToken,
            Task<List<Activity>>> GetBySemesterHandler { get; set; } =
                (_, _, _, _) => Task.FromResult(new List<Activity>());

        public Func<
            int,
            int,
            string,
            CancellationToken,
            Task<SemesterSnapshotResponse>> GetSnapshotHandler { get; set; } =
                (_, semesterId, _, _) => Task.FromResult(
                    new SemesterSnapshotResponse
                    {
                        SemesterId = semesterId
                    });

        public Task<List<Activity>?> GetActivitiesAsync(
            int userId,
            int semesterId,
            int classId,
            string token,
            CancellationToken cancellationToken = default)
        {
            return GetByClassHandler(
                userId,
                semesterId,
                classId,
                token,
                cancellationToken);
        }

        public Task<List<Activity>> GetActivitiesBySemesterAsync(
            int userId,
            int semesterId,
            string token,
            CancellationToken cancellationToken = default)
        {
            return GetBySemesterHandler(
                userId,
                semesterId,
                token,
                cancellationToken);
        }

        public Task<SemesterSnapshotResponse> GetSemesterSnapshotAsync(
            int userId,
            int semesterId,
            string token,
            CancellationToken cancellationToken = default)
        {
            return GetSnapshotHandler(
                userId,
                semesterId,
                token,
                cancellationToken);
        }
    }

    private sealed class UnsupportedUserService : IUserService
    {
        public Task<User?> GetUserByCredentialsAsync(
            Credentials credentials,
            Guid accessKeyId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string?> GetCookieAsync(
            Credentials credentials,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class UnsupportedClassService : IClassService
    {
        public Task<List<ClassInfo>?> GetClassesAsync(
            int semesterId,
            string token,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class UnsupportedSemesterService : ISemesterService
    {
        public Task<List<int>?> GetSemestersAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
