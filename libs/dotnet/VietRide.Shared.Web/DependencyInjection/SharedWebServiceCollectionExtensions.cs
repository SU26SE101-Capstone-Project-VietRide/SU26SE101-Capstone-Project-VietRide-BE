using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
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

        // Controllers + global ApiResponse envelope filters (ADR 0004).
        // ApiResponseExceptionFilter replaces ProblemDetailsExceptionFilter (RFC 7807 removed).
        // ApiResponseResultFilter (IAlwaysRunResultFilter) auto-wraps ObjectResult success values.
        services.AddControllers(o =>
        {
            o.Filters.Add<ApiResponseExceptionFilter>();
            o.Filters.Add<ApiResponseResultFilter>();
        });

        // Model-binding failures (malformed JSON, missing required fields, type mismatches) route
        // through the same error envelope via InvalidModelStateResponseFactory (ADR 0004 Rule 5).
        services.Configure<ApiBehaviorOptions>(o =>
        {
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var traceId = GetTraceId(ctx.HttpContext);

                var fields = ctx.ModelState
                    .Where(kv => kv.Value?.Errors.Count > 0)
                    .SelectMany(kv => kv.Value!.Errors.Select(e =>
                        new ApiFieldError(kv.Key, string.IsNullOrWhiteSpace(e.ErrorMessage)
                            ? "Invalid value."
                            : e.ErrorMessage)))
                    .ToList();

                var error = new ApiError
                {
                    Code = "VALIDATION_ERROR",
                    Message = "One or more validation errors occurred.",
                    Fields = fields.Count > 0 ? fields : null,
                };

                var envelope = ApiResponse.Failure(422, error, ApiMeta.Create(traceId));
                return new ObjectResult(envelope) { StatusCode = 422 };
            };
        });

        // Health + Swagger
        services.AddVietRideHealthChecks(configuration);
        services.AddVietRideSwagger(serviceName);

        // Observability v1 stack per BACKEND_SOURCE_OF_TRUTH §9.13 = Sentry + UptimeRobot + Serilog only.
        // Prometheus/Grafana/Jaeger/Tempo/Loki/OpenTelemetry deferred to v2.

        return services;
    }

    private static string GetTraceId(HttpContext context)
    {
        if (context.Items.TryGetValue(Middleware.RequestLoggingMiddleware.RequestIdHeader, out var id)
            && id is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return context.Request.Headers.TryGetValue(Middleware.RequestLoggingMiddleware.RequestIdHeader, out var h)
            ? h.ToString()
            : string.Empty;
    }
}
