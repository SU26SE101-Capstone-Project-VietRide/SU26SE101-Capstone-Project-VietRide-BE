using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Shared.Web.Swagger;

public sealed class AuthorizeOperationFilter : IOperationFilter
{
    private const string UserAccessTokenScheme = "UserAccessToken";
    private const string InternalJwtScheme = "InternalJwt";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<AllowAnonymousAttribute>().Any())
            return;

        var authorizeAttributes = metadata.OfType<AuthorizeAttribute>().ToArray();
        if (authorizeAttributes.Length == 0)
            return;

        var schemeName = authorizeAttributes.Any(UsesInternalJwt)
            ? InternalJwtScheme
            : UserAccessTokenScheme;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = schemeName,
                },
            }] = [],
        });
    }

    private static bool UsesInternalJwt(AuthorizeAttribute attribute)
        => attribute.AuthenticationSchemes?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(InternalJwtAuthenticationExtensions.Scheme, StringComparer.Ordinal) == true;
}
