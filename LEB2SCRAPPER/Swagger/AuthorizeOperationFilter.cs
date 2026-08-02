using LEB2SCRAPPER.Authentication;
using LEB2SCRAPPER.Presentation.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LEB2SCRAPPER.Swagger;

public sealed class AuthorizeOperationFilter : IOperationFilter
{
    private const string Leb2UserIdHeaderName = "X-LEB2-USER-ID";

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
}
