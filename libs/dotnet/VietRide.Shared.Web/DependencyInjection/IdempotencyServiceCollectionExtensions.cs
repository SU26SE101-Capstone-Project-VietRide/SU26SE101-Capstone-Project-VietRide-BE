using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Shared.Web.DependencyInjection;

/// <summary>
/// Configuration for <see cref="IdempotencyMiddleware"/>.
/// The Redis key pattern is <c>&lt;service&gt;:idem:{key}</c> (BSOT §5.6).
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// Service-scoped Redis key prefix. Each real service sets its own (e.g. "booking", "payment")
    /// when it wires the middleware in Sprint 3. Defaults to the placeholder "svc".
    /// </summary>
    public string ServicePrefix { get; set; } = "svc";
}

/// <summary>
/// OPT-IN registration helpers for <see cref="IdempotencyMiddleware"/>.
///
/// PLACEHOLDER for Sprint-3 reuse — intentionally NOT called by
/// <c>AddVietRideSharedWeb</c> or any running service pipeline this day.
/// A service opts in via <c>services.AddVietRideIdempotency("booking")</c> and
/// <c>app.UseVietRideIdempotency()</c>. It assumes an
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
        string servicePrefix = "svc")
    {
        services.AddSingleton(new IdempotencyOptions { ServicePrefix = servicePrefix });
        return services;
    }

    /// <summary>Adds <see cref="IdempotencyMiddleware"/> to the request pipeline (opt-in).</summary>
    public static IApplicationBuilder UseVietRideIdempotency(this IApplicationBuilder app)
        => app.UseMiddleware<IdempotencyMiddleware>();
}
