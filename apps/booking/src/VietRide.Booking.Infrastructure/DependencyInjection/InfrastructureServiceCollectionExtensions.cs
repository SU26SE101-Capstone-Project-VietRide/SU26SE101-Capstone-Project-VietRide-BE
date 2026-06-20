using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Services;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Booking.Infrastructure.Persistence.Repositories;
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
        var redisUrl = configuration["REDIS_URL"]
            ?? Environment.GetEnvironmentVariable("REDIS_URL")
            ?? "localhost:6379";
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
        if (UseTripDevStub(configuration))
        {
            services.AddScoped<ITripServiceClient, DevTripServiceClient>();
        }
        else
        {
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
        }

        // Repositories (Task 12.3)
        services.AddScoped<IBookingRepository, BookingRepository>();

        // Repositories (Task 14.1)
        services.AddScoped<IVoucherRepository, VoucherRepository>();

        // Repositories (Task 14.2)
        services.AddScoped<IOperatorVoucherConsentRepository, OperatorVoucherConsentRepository>();

        // Application service (Task 12.3)
        // BookingService lives in Application layer; registered here because its ctor
        // depends on ITripServiceClient which is Infrastructure.
        services.AddScoped<IBookingService, BookingService>();

        // Application service (Task 14.1)
        // VoucherCodeGenerator is stateless — registered as singleton.
        services.AddSingleton<IVoucherCodeGenerator, VoucherCodeGenerator>();

        // Payment inter-service client (real debit lands Day 15/16).
        // Day-12 local/runtime seam can be enabled explicitly so WALLET reaches CONFIRMED
        // without requiring the future Payment charge endpoint.
        // BSOT §3.5 line 427/479: interface at Abstractions/ServiceClients/, impl at Infrastructure/Http/.
        if (UsePaymentDevStub(configuration))
        {
            services.AddScoped<IPaymentServiceClient, DevPaymentServiceClient>();
        }
        else
        {
            services
                .AddHttpClient<IPaymentServiceClient, PaymentServiceClient>(client =>
                {
                    var baseUrl = ResolvePaymentBaseUrl(configuration);
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
        }

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

    private static bool UseTripDevStub(IConfiguration configuration)
        => configuration.GetValue("Trip:UseDevStub", false)
            || string.Equals(
                Environment.GetEnvironmentVariable("BOOKING_TRIP_USE_DEV_STUB"),
                "true",
                StringComparison.OrdinalIgnoreCase);

    private static bool UsePaymentDevStub(IConfiguration configuration)
        => configuration.GetValue("Payment:UseDevStub", false)
            || string.Equals(
                Environment.GetEnvironmentVariable("BOOKING_PAYMENT_USE_DEV_STUB"),
                "true",
                StringComparison.OrdinalIgnoreCase);

    private static string ResolvePaymentBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration["Payment:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("PAYMENT_SERVICE_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Payment base URL must be configured via Payment:BaseUrl or PAYMENT_SERVICE_BASE_URL.");
        }

        return baseUrl;
    }
}
