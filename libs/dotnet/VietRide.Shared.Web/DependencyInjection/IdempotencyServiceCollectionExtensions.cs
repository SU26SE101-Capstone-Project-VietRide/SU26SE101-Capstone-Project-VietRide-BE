using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Shared.Web.DependencyInjection;

/// <summary>
/// Configuration for <see cref="IdempotencyMiddleware"/>.
/// Redis v2 uses separate hashed-key response and processing namespaces (BSOT §5.6).
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// Service-scoped Redis key prefix. Each real service sets its own (e.g. "booking", "payment")
    /// when it wires the middleware. Defaults to "svc" for controlled test pipelines.
    /// </summary>
    public string ServicePrefix { get; set; } = "svc";

    /// <summary>
    /// Requires a valid UUID-v4 key for every POST/PATCH/PUT/DELETE endpoint unless it has an
    /// explicit <see cref="SkipIdempotencyAttribute"/> exemption.
    /// </summary>
    public bool RequireAllMutations { get; set; }
}

/// <summary>
/// Opt-in registration helpers for <see cref="IdempotencyMiddleware"/>.
/// A service opts in via <c>services.AddVietRideIdempotency("booking")</c> and
/// <c>app.UseVietRideIdempotency()</c>. Endpoints requiring mandatory UUID-v4 validation carry
/// <see cref="RequireIdempotencyAttribute"/> metadata. Run the middleware after routing and
/// authentication so endpoint metadata and the authenticated subject are available. The middleware
/// also honors supplied keys on POST/PATCH/PUT/DELETE endpoints without mandatory metadata. It assumes an
/// <see cref="StackExchange.Redis.IConnectionMultiplexer"/> is already registered.
/// </summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IdempotencyOptions"/> with the given service prefix.
    /// Does NOT register the Redis connection multiplexer — the owning service must do that.
    /// </summary>
    public static IServiceCollection AddVietRideIdempotency(
        this IServiceCollection services,
        string servicePrefix = "svc",
        bool requireAllMutations = false)
    {
        services.AddSingleton(new IdempotencyOptions
        {
            ServicePrefix = servicePrefix,
            RequireAllMutations = requireAllMutations,
        });
        return services;
    }

    /// <summary>
    /// Adds <see cref="IdempotencyMiddleware"/> to the request pipeline after routing and authentication.
    /// </summary>
    public static IApplicationBuilder UseVietRideIdempotency(this IApplicationBuilder app)
        => app.UseMiddleware<IdempotencyMiddleware>();
}
