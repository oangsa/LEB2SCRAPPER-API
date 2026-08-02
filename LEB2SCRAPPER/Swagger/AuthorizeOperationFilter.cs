using LEB2SCRAPPER.Authentication;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;
using LEB2SCRAPPER.Middleware;
using LEB2SCRAPPER.Presentation.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LEB2SCRAPPER.Swagger;

public sealed class AuthorizeOperationFilter : IOperationFilter
{
    private const string Leb2UserIdHeaderName = "X-LEB2-USER-ID";
    private readonly DeviceBindingOptions _deviceBindingOptions;
    private readonly ClientCompatibilityOptions _clientCompatibilityOptions;

    public AuthorizeOperationFilter(
        DeviceBindingOptions deviceBindingOptions,
        ClientCompatibilityOptions clientCompatibilityOptions)
    {
        _deviceBindingOptions = deviceBindingOptions;
        _clientCompatibilityOptions = clientCompatibilityOptions;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In == ParameterLocation.Header
                && string.Equals(
                    parameter.Name,
                    Leb2UserIdHeaderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description =
                    "Compatibility assertion for the authoritative LEB2 User.Id. "
                    + "Must match the identity bound to access-key.";
            }
        }

        var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (endpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var hasLeb2Authorization = endpointMetadata
            .OfType<IAuthorizeData>()
            .Any();
        var hasAccessKeyAuthorization = endpointMetadata
            .OfType<AccessKeyAuthorizeAttribute>()
            .Any();

        if (!hasLeb2Authorization && !hasAccessKeyAuthorization)
        {
            return;
        }

        AddHeaderParameter(
            operation,
            AccessKeyAuthorizationFilter.DeviceIdHeaderName,
            "Opaque device identifier. It is HMAC-bound server-side and never persisted in raw form.",
            _deviceBindingOptions.EnforcementEnabled);
        AddHeaderParameter(
            operation,
            AccessKeyAuthorizationFilter.DeviceNameHeaderName,
            "Optional device display name.",
            false);
        AddHeaderParameter(
            operation,
            AccessKeyAuthorizationFilter.DevicePlatformHeaderName,
            "Optional device platform.",
            false);
        AddHeaderParameter(
            operation,
            AccessKeyAuthorizationFilter.DeviceOsVersionHeaderName,
            "Optional device operating-system version.",
            false);
        AddHeaderParameter(
            operation,
            ClientCompatibilityMiddleware.ClientVersionHeaderName,
            "Frontend application version, separate from API version.",
            _clientCompatibilityOptions.EnforcementEnabled);
        operation.Responses.TryAdd(
            StatusCodes.Status426UpgradeRequired.ToString(),
            new OpenApiResponse
            {
                Description = "Client update required when compatibility enforcement is enabled."
            });

        var securityRequirement = new OpenApiSecurityRequirement();

        if (hasLeb2Authorization)
        {
            securityRequirement[
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = Leb2BearerDefaults.AuthenticationScheme
                    }
                }] = Array.Empty<string>();
        }

        if (hasAccessKeyAuthorization)
        {
            securityRequirement[
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "AccessKey"
                    }
                }] = Array.Empty<string>();
        }

        operation.Security.Add(securityRequirement);
    }

    private static void AddHeaderParameter(
        OpenApiOperation operation,
        string name,
        string description,
        bool required)
    {
        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Header
                && string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Description = description,
            Required = required,
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
