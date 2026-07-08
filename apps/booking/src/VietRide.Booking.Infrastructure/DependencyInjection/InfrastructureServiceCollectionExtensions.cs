using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Services;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Booking.Infrastructure.Persistence.Repositories;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;

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
        IConfiguration configuration,
        bool registerConsumers = true)
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

        // Identity operator lookup client (Task 17.0).
        if (UseIdentityDevStub(configuration))
        {
            services.AddScoped<IOperatorServiceClient, DevOperatorServiceClient>();
        }
        else
        {
            services
                .AddHttpClient<IOperatorServiceClient, OperatorServiceClient>(client =>
                {
                    var baseUrl = ResolveIdentityBaseUrl(configuration);
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

        // Repositories (Task 17.3)
        services.AddScoped<IBookingStatsRepository, BookingStatsRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();

        // Application service (Task 12.3)
        // BookingService lives in Application layer; registered here because its ctor
        // depends on ITripServiceClient which is Infrastructure.
        services.AddScoped<IBookingService, BookingService>();

        // Application service (Task 14.1)
        // VoucherCodeGenerator is stateless — registered as singleton.
        services.AddSingleton<IVoucherCodeGenerator, VoucherCodeGenerator>();

        // Application service (Task 14.3)
        // VoucherService validates + applies vouchers at checkout; scoped because it depends
        // on scoped repositories (IVoucherRepository, IOperatorVoucherConsentRepository).
        services.AddScoped<IVoucherService, VoucherService>();

        if (registerConsumers)
        {
            services.AddVietRideEventConsumer<PaymentSucceededIntegrationEvent, PaymentSucceededIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.payment-succeeded";
                options.BindingKeys = [PaymentSucceededIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<PaymentExpiredIntegrationEvent, PaymentExpiredIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.payment-expired";
                options.BindingKeys = [PaymentExpiredIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<WalletCreditedIntegrationEvent, WalletCreditedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.wallet-credited";
                options.BindingKeys = [WalletCreditedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<BookingConfirmedIntegrationEvent, BookingConfirmedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.booking-confirmed-stats";
                options.BindingKeys = [BookingConfirmedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<BookingCancelledIntegrationEvent, BookingCancelledIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.booking-cancelled-stats";
                options.BindingKeys = [BookingCancelledIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<BookingRefundedIntegrationEvent, BookingRefundedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.booking-refunded-stats";
                options.BindingKeys = [BookingRefundedIntegrationEvent.EventType];
            });
        }

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

    private static bool UseIdentityDevStub(IConfiguration configuration)
        => configuration.GetValue("Identity:UseDevStub", false)
            || string.Equals(
                Environment.GetEnvironmentVariable("BOOKING_IDENTITY_USE_DEV_STUB"),
                "true",
                StringComparison.OrdinalIgnoreCase);

    private static bool UsePaymentDevStub(IConfiguration configuration)
        => configuration.GetValue("Payment:UseDevStub", false)
            || string.Equals(
                Environment.GetEnvironmentVariable("BOOKING_PAYMENT_USE_DEV_STUB"),
                "true",
                StringComparison.OrdinalIgnoreCase);

    private static string ResolveIdentityBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration["Identity:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("IDENTITY_SERVICE_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Identity base URL must be configured via Identity:BaseUrl or IDENTITY_SERVICE_BASE_URL.");
        }

        return baseUrl;
    }

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
