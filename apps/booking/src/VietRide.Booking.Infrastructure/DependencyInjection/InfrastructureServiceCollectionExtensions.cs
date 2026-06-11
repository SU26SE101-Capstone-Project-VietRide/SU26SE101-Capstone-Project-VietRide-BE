using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.DependencyInjection;

/// <summary>
/// Registers Booking Infrastructure services such as repositories, external clients,
/// and Redis (required by the idempotency middleware).
/// </summary>
/// <remarks>
/// DB-CONTEXT GUARD: this method MUST NOT call AddVietRideDbContext / AddDbContext.
/// The BookingDbContext is already registered at Program.cs via AddVietRideDbContext.
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

        // Internal JWT provider — used by outbound delegating handlers.
        services.AddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.AddTransient<InternalJwtDelegatingHandler>();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        // Trip inter-service HTTP client (Task 12.2).
        // BSOT §3.5 line 935: ITripServiceClient at Abstractions/ServiceClients/,
        // impl TripServiceClient at Infrastructure/Http/.
        services
            .AddHttpClient<ITripServiceClient, TripServiceClient>(client =>
            {
                var baseUrl = ResolveTripBaseUrl(configuration);
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>()
            .AddPolicyHandler(HttpResiliencePolicies.GetRetryPolicy())
            .AddPolicyHandler(HttpResiliencePolicies.GetCircuitBreakerPolicy());

        // Repositories, IBookingService, IPaymentServiceClient
        // are registered in Task 12.3 (depend on domain entities that land in 12.1).

        return services;
    }

    private static string ResolveTripBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration["Trip:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("TRIP_SERVICE_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Trip base URL must be configured via Trip:BaseUrl or TRIP_SERVICE_BASE_URL.");
        }

        return baseUrl;
    }
}
