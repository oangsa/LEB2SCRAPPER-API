using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;
using LEB2SCRAPPER.Service.Contracts.Master;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LEB2SCRAPPER.Presentation.Filters;

public enum AccessKeyRequirement
{
    Provisioned,
    Activated,
    ActivatedAllowUnboundDevice
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
            Requirement,
            serviceProvider.GetRequiredService<DeviceBindingRequestContext>());
    }
}

public sealed class AccessKeyAuthorizationFilter : IAsyncAuthorizationFilter
{
    public const string HeaderName = "access-key";
    public const string DeviceIdHeaderName = "X-Device-ID";
    public const string DeviceNameHeaderName = "X-Device-Name";
    public const string DevicePlatformHeaderName = "X-Device-Platform";
    public const string DeviceOsVersionHeaderName = "X-Device-OS-Version";

    private readonly IAccessKeyService _accessKeyService;
    private readonly AccessKeyRequestContext _requestContext;
    private readonly AccessKeyRequirement _requirement;
    private readonly DeviceBindingRequestContext? _deviceBindingRequestContext;

    public AccessKeyAuthorizationFilter(
        IAccessKeyService accessKeyService,
        AccessKeyRequestContext requestContext,
        AccessKeyRequirement requirement,
        DeviceBindingRequestContext? deviceBindingRequestContext = null)
    {
        _accessKeyService = accessKeyService;
        _requestContext = requestContext;
        _requirement = requirement;
        _deviceBindingRequestContext = deviceBindingRequestContext;
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

        var state = _requirement is AccessKeyRequirement.Activated
            or AccessKeyRequirement.ActivatedAllowUnboundDevice
            ? await _accessKeyService.ValidateActivatedKeyAsync(
                keyId,
                context.HttpContext.RequestAborted)
            : await _accessKeyService.ValidateProvisionedKeyAsync(
                keyId,
                context.HttpContext.RequestAborted);

        var deviceBinding = ReadDeviceBindingRequest(context.HttpContext.Request);
        if (deviceBinding is not null)
        {
            _deviceBindingRequestContext?.Set(deviceBinding);
        }
        await _accessKeyService.EnsureDeviceBindingAsync(
            state,
            deviceBinding,
            _requirement is AccessKeyRequirement.Provisioned
                or AccessKeyRequirement.ActivatedAllowUnboundDevice,
            context.HttpContext.RequestAborted);

        _requestContext.Set(state);
    }

    private static DeviceBindingRequest? ReadDeviceBindingRequest(
        HttpRequest request)
    {
        var deviceId = ReadHeader(request, DeviceIdHeaderName);

        if (deviceId is null)
        {
            return null;
        }

        return new DeviceBindingRequest(
            deviceId,
            ReadHeader(request, DeviceNameHeaderName),
            ReadHeader(request, DevicePlatformHeaderName),
            ReadHeader(request, DeviceOsVersionHeaderName),
            ReadHeader(request, ClientCompatibilityOptions.ClientVersionHeaderName));
    }

    private static string? ReadHeader(
        HttpRequest request,
        string name)
    {
        var values = request.Headers[name];

        if (values.Count > 1)
        {
            throw new DeviceIdInvalidException();
        }

        if (values.Count == 0 || string.IsNullOrWhiteSpace(values[0]))
        {
            return null;
        }

        return values[0]!.Trim();
    }
}
