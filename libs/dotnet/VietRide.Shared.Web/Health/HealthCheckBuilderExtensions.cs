using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace VietRide.Shared.Web.Health;

/// /health (liveness — process is up) + /ready (readiness — DB/Redis/RabbitMQ reachable).
/// Per BACKEND_SOURCE_OF_TRUTH 5.1.
public static class HealthCheckBuilderExtensions
{
    /// <summary>
    /// Registers liveness placeholder + readiness probes for Postgres, Redis, RabbitMQ.
    /// Each probe is wired ONLY if the relevant connection string env var is present, so
    /// services that don't use a dependency don't fail readiness for missing it.
    /// </summary>
    public static IServiceCollection AddVietRideHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddHealthChecks();

        // Postgres — required by all 5 .NET services. Read from ConnectionStrings:Default.
        var pgConn = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(pgConn))
        {
            builder.AddNpgSql(
                connectionString: pgConn,
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "db" });
        }

        // Redis — optional per service.
        var redisHost = configuration["REDIS_HOST"];
        var redisPort = configuration["REDIS_PORT"] ?? "6379";
        var redisPass = configuration["REDIS_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(redisHost))
        {
            var redisConn = string.IsNullOrEmpty(redisPass)
                ? $"{redisHost}:{redisPort}"
                : $"{redisHost}:{redisPort},password={redisPass}";
            builder.AddRedis(
                redisConnectionString: redisConn,
                name: "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "cache" });
        }

        // RabbitMQ — optional per service (only services that publish/consume).
        var rabbitHost = configuration["RABBITMQ_HOST"];
        if (!string.IsNullOrWhiteSpace(rabbitHost))
        {
            var rabbitUser = configuration["RABBITMQ_USER"] ?? "guest";
            var rabbitPass = configuration["RABBITMQ_PASSWORD"] ?? "guest";
            var rabbitPort = configuration["RABBITMQ_PORT"] ?? "5672";
            var rabbitVhost = configuration["RABBITMQ_VHOST"] ?? "/";
            var rabbitUri = $"amqp://{rabbitUser}:{rabbitPass}@{rabbitHost}:{rabbitPort}{rabbitVhost}";
            builder.AddRabbitMQ(
                rabbitConnectionString: rabbitUri,
                name: "rabbitmq",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "queue" });
        }

        return services;
    }

    public static IEndpointRouteBuilder MapVietRideHealth(this IEndpointRouteBuilder endpoints, string serviceName)
    {
        // /health — liveness only (NO dependency check; just "process responding"). Used by Docker HEALTHCHECK.
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = (ctx, _) => WriteJson(ctx, new { status = "ok", service = serviceName }),
        });

        // /ready — readiness (run probes tagged 'ready'). Used by K8s readiness gate / load balancer.
        endpoints.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = (ctx, report) => WriteJson(ctx, new
            {
                status = report.Status.ToString().ToLowerInvariant(),
                service = serviceName,
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString().ToLowerInvariant(),
                    durationMs = e.Value.Duration.TotalMilliseconds,
                    description = e.Value.Description,
                    error = e.Value.Exception?.Message,
                }),
            }),
        });

        return endpoints;
    }

    private static Task WriteJson(HttpContext ctx, object payload)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
