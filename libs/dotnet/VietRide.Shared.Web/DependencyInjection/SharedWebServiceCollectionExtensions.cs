using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Filters;
using VietRide.Shared.Web.Health;
using VietRide.Shared.Web.Swagger;

namespace VietRide.Shared.Web.DependencyInjection;

/// One-stop registration of shared cross-cutting for every .NET service.
/// Called from each service's Program.cs: builder.Services.AddVietRideSharedWeb(builder.Configuration, "Identity");
public static class SharedWebServiceCollectionExtensions
{
    public static IServiceCollection AddVietRideSharedWeb(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        // Clock (IClock — testable DateTimeOffset.UtcNow)
        services.AddSingleton<IClock, VietRide.Shared.Kernel.Abstractions.SystemClock>();

        // Internal JWT auth (X-Internal-Auth header, HS256, 120s)
        var secret = configuration["INTERNAL_JWT_SECRET"]
            ?? throw new InvalidOperationException("INTERNAL_JWT_SECRET not configured");

        services.AddAuthentication(InternalJwtAuthenticationExtensions.Scheme)
            .AddInternalJwt(secret);
        services.AddAuthorization();

        // Controllers + global Problem+JSON filter
        services.AddControllers(o =>
        {
            o.Filters.Add<ProblemDetailsExceptionFilter>();
        });

        // Health + Swagger
        services.AddVietRideHealthChecks(configuration);
        services.AddVietRideSwagger(serviceName);

        // Observability v1 stack per BACKEND_SOURCE_OF_TRUTH §9.13 = Sentry + UptimeRobot + Serilog only.
        // Prometheus/Grafana/Jaeger/Tempo/Loki/OpenTelemetry deferred to v2.

        return services;
    }
}
