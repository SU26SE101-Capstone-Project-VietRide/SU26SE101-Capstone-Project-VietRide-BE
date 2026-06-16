using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace VietRide.Shared.Web.Swagger;

/// OpenAPI / Swagger UI defaults across all .NET services.
public static class SwaggerSetupExtensions
{
    public static IServiceCollection AddVietRideSwagger(this IServiceCollection services, string serviceName, string version = "v1")
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(version, new OpenApiInfo
            {
                Title = $"VietRide {serviceName} API",
                Version = version,
                Description = "VietRide capstone — internal/public endpoints. See BACKEND_SOURCE_OF_TRUTH §5 for conventions.",
            });

            c.AddSecurityDefinition("UserAccessToken", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "User Access Token (RS256). Public endpoints only.",
            });

            c.AddSecurityDefinition("InternalJwt", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "X-Internal-Auth",
                In = ParameterLocation.Header,
                Description = "Internal JWT (HS256, 120s). For /internal/* endpoints only.",
            });

            c.OperationFilter<AuthorizeOperationFilter>();
        });

        return services;
    }

    public static IApplicationBuilder UseVietRideSwagger(this IApplicationBuilder app)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }
}
