using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LEB2SCRAPPER.Authentication;

public sealed class Leb2BearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string BearerPrefix = "Bearer ";
    private readonly ILeb2SessionCredentialStore _credentialStore;

    public Leb2BearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ILeb2SessionCredentialStore credentialStore)
        : base(options, logger, encoder)
    {
        _credentialStore = credentialStore;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (Request.Headers.Authorization.Count > 1)
        {
            return Task.FromResult(
                AuthenticateResult.Fail("Exactly one Authorization header is required."));
        }

        var authorization = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Task.FromResult(AuthenticateResult.Fail("The Authorization header is empty."));
        }

        if (authorization.Equals(
            BearerPrefix.TrimEnd(),
            StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("The bearer credential is empty."));
        }

        var sessionCookie = authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[BearerPrefix.Length..].Trim()
            : authorization.Trim();

        if (string.IsNullOrWhiteSpace(sessionCookie))
        {
            return Task.FromResult(AuthenticateResult.Fail("The bearer credential is empty."));
        }

        _credentialStore.Set(sessionCookie);
        Response.OnCompleted(() =>
        {
            _credentialStore.Clear();
            return Task.CompletedTask;
        });

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "LEB2 session") },
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        Response.Headers.WWWAuthenticate = "Bearer";

        var response = new ErrorResponse
        {
            Message = "A LEB2 bearer session is required.",
            ResponseCode = ApiErrorCodes.AuthenticationRequired,
            TraceId = Context.TraceIdentifier
        };

        await Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
