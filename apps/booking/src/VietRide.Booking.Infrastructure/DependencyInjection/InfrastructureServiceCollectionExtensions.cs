using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.DependencyInjection;

/// <summary>
/// Registers Booking Infrastructure services such as repositories, external clients,
/// and Redis (required by the idempotency middleware).
/// </summary>
/// <remarks>
/// DB-CONTEXT GUARD: this method MUST NOT call AddVietRideDbContext / AddDbContext.
/// The BookingDbContext is already registered at Program.cs via AddVietRideDbContext.
/// Repositories and the Trip HTTP client are added in subsequent tasks (12.1 / 12.2 / 12.3).
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds Booking Infrastructure services to the DI container.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Redis — required by IdempotencyMiddleware (wired in Program.cs via AddVietRideIdempotency).
        // Falls back gracefully if REDIS_URL is absent (AbortOnConnectFail = false).
        var redisUrl = configuration["REDIS_URL"] ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisOptions));

        // Internal JWT provider — used by outbound delegating handlers registered in 12.2.
        services.AddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.AddTransient<InternalJwtDelegatingHandler>();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        // Repositories, IBookingService, ITripServiceClient, IPaymentServiceClient
        // are registered in Tasks 12.1 / 12.2 / 12.3 (they depend on domain entities
        // and the Trip seam that land in those tasks).

        return services;
    }
}
