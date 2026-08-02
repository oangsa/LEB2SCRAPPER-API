using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Service.Contracts.Master;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace LEB2SCRAPPER.Tests.Authorization;

public class AccessKeyAuthorizationFilterTests
{
    private static readonly Guid KeyId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task MissingHeader_RequiresAccessKey()
    {
        var context = CreateContext();
        var filter = CreateFilter(AccessKeyRequirement.Provisioned);

        await Assert.ThrowsAsync<AccessKeyRequiredException>(() =>
            filter.OnAuthorizationAsync(context));
    }

    [Fact]
    public async Task MalformedHeader_RejectsAccessKey()
    {
        var context = CreateContext();
        context.HttpContext.Request.Headers[AccessKeyAuthorizationFilter.HeaderName] =
            "not-a-uuid";
        var filter = CreateFilter(AccessKeyRequirement.Provisioned);

        await Assert.ThrowsAsync<AccessKeyInvalidException>(() =>
            filter.OnAuthorizationAsync(context));
    }

    [Fact]
    public async Task ProvisionedRequirement_AllowsUnassignedKeyAndStoresContext()
    {
        var requestContext = new AccessKeyRequestContext();
        var context = CreateContext();
        context.HttpContext.Request.Headers[AccessKeyAuthorizationFilter.HeaderName] =
            KeyId.ToString();
        var filter = new AccessKeyAuthorizationFilter(
            new StubAccessKeyService(new AccessKeyState(KeyId, null, null, null)),
            requestContext,
            AccessKeyRequirement.Provisioned);

        await filter.OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.Equal(KeyId, requestContext.Current?.KeyId);
    }

    [Fact]
    public async Task ActivatedRequirement_RejectsUnassignedKey()
    {
        var context = CreateContext();
        context.HttpContext.Request.Headers[AccessKeyAuthorizationFilter.HeaderName] =
            KeyId.ToString();
        var filter = CreateFilter(
            AccessKeyRequirement.Activated,
            new AccessKeyState(KeyId, null, null, null));

        await Assert.ThrowsAsync<AccessKeyNotActivatedException>(() =>
            filter.OnAuthorizationAsync(context));
    }

    private static AuthorizationFilterContext CreateContext()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, []);
    }

    private static AccessKeyAuthorizationFilter CreateFilter(
        AccessKeyRequirement requirement,
        AccessKeyState? state = null)
    {
        return new AccessKeyAuthorizationFilter(
            new StubAccessKeyService(
                state ?? new AccessKeyState(KeyId, UserId, "student-001", 1001)),
            new AccessKeyRequestContext(),
            requirement);
    }

    private sealed class StubAccessKeyService : IAccessKeyService
    {
        private readonly AccessKeyState _state;

        public StubAccessKeyService(AccessKeyState state)
        {
            _state = state;
        }

        public Task<AccessKeyState> ValidateProvisionedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_state);
        }

        public void EnsureStudentIdentity(
            AccessKeyState state,
            string studentId)
        {
        }

        public void EnsureLeb2IdentityInitialized(AccessKeyState state)
        {
        }

        public void EnsureLeb2UserIdentity(
            AccessKeyState state,
            int leb2UserId)
        {
        }

        public Task<AccessKeyState> ValidateActivatedKeyAsync(
            Guid keyId,
            CancellationToken cancellationToken = default)
        {
            if (!_state.IsAssigned)
            {
                throw new AccessKeyNotActivatedException();
            }

            return Task.FromResult(_state);
        }

        public Task RegisterSuccessfulLoginAsync(
            Guid keyId,
            string studentId,
            int leb2UserId,
            string name,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
