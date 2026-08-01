using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Service.Contracts.Master;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace LEB2SCRAPPER.Presentation.Filters;

public enum AccessKeyRequirement
{
    Provisioned,
    Activated
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class AccessKeyAuthorizeAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public AccessKeyAuthorizeAttribute(AccessKeyRequirement requirement)
    {
        Requirement = requirement;
    }

    public AccessKeyRequirement Requirement { get; }

    public bool IsReusable => false;

    public int Order => int.MinValue;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        return new AccessKeyAuthorizationFilter(
            serviceProvider.GetRequiredService<IAccessKeyService>(),
            serviceProvider.GetRequiredService<AccessKeyRequestContext>(),
            Requirement);
    }
}

public sealed class AccessKeyAuthorizationFilter : IAsyncAuthorizationFilter
{
    public const string HeaderName = "access-key";

    private readonly IAccessKeyService _accessKeyService;
    private readonly AccessKeyRequestContext _requestContext;
    private readonly AccessKeyRequirement _requirement;

    public AccessKeyAuthorizationFilter(
        IAccessKeyService accessKeyService,
        AccessKeyRequestContext requestContext,
        AccessKeyRequirement requirement)
    {
        _accessKeyService = accessKeyService;
        _requestContext = requestContext;
        _requirement = requirement;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var headerValues = context.HttpContext.Request.Headers[HeaderName];

        if (headerValues.Count == 0)
        {
            throw new AccessKeyRequiredException();
        }

        if (headerValues.Count != 1
            || !Guid.TryParse(headerValues[0], out var keyId)
            || keyId == Guid.Empty)
        {
            throw new AccessKeyInvalidException();
        }

        var state = _requirement == AccessKeyRequirement.Activated
            ? await _accessKeyService.ValidateActivatedKeyAsync(
                keyId,
                context.HttpContext.RequestAborted)
            : await _accessKeyService.ValidateProvisionedKeyAsync(
                keyId,
                context.HttpContext.RequestAborted);

        _requestContext.Set(state);
    }
}
