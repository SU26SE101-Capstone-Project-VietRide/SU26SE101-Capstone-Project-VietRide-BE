using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.Caching;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Services;
using VietRide.Booking.Infrastructure.Caching;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Booking.Infrastructure.Persistence.Repositories;
using VietRide.Booking.Infrastructure.Services;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Reporting;

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
        services.AddSingleton<IExcelReportWriter, ClosedXmlExcelReportWriter>();
        services.AddScoped<IPlatformReportCache, RedisPlatformReportCache>();
        services.AddHttpClient<ITripPlatformReportClient, TripPlatformReportClient>(client =>
            ConfigurePlatformReportClient(client, ResolveTripBaseUrl(configuration)))
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
        services.AddHttpClient<IParcelPlatformReportClient, ParcelPlatformReportClient>(client =>
            ConfigurePlatformReportClient(client, ResolveParcelBaseUrl(configuration)))
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
        services.AddHttpClient<IPaymentPlatformLedgerClient, PaymentPlatformLedgerClient>(client =>
            ConfigurePlatformReportClient(client, ResolvePaymentBaseUrl(configuration)))
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
        services.AddHttpClient<IIdentityPlatformReportClient, IdentityPlatformReportClient>(client =>
            ConfigurePlatformReportClient(client, ResolveIdentityBaseUrl(configuration)))
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
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

        services
            .AddHttpClient<IIdentityUserServiceClient, IdentityUserServiceClient>(client =>
            {
                var baseUrl = ResolveIdentityBaseUrl(configuration);
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>()
            .AddPolicyHandler(CreateIdentityUserRetryPolicy())
            .AddPolicyHandler(HttpResiliencePolicies.GetCircuitBreakerPolicy());

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
        services.AddScoped<IBookingStatusHistoryRepository, BookingStatusHistoryRepository>();
        services.AddScoped<IBookingPendingActionRepository, BookingPendingActionRepository>();
        services.AddScoped<IBookingStationRedirectRepository, BookingStationRedirectRepository>();
        services.AddScoped<IBookingStationCanonicalizer, BookingStationCanonicalizer>();

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
            services.AddVietRideEventConsumer<StopDisabledIntegrationEvent, StopDisabledIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.stop-disabled";
                options.BindingKeys = [StopDisabledIntegrationEvent.EventTypeValue];
            });
            services.AddVietRideEventConsumer<StationMergedIntegrationEvent, StationMergedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.station-merged";
                options.BindingKeys = [StationMergedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<TripVehicleSwappedIntegrationEvent, TripVehicleSwappedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.trip-vehicle-swapped";
                options.BindingKeys = [TripVehicleSwappedIntegrationEvent.EventTypeValue];
            });
            services.AddVietRideEventConsumer<TripScheduleChangedIntegrationEvent, TripScheduleChangedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.trip-schedule-changed";
                options.BindingKeys = [TripScheduleChangedIntegrationEvent.EventTypeValue];
            });
            services.AddVietRideEventConsumer<TripCancelledByOperatorIntegrationEvent, TripCancelledByOperatorIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.trip-cancelled";
                options.BindingKeys = [TripCancelledByOperatorIntegrationEvent.EventTypeValue];
            });
            services.AddVietRideEventConsumer<PaymentExpiredIntegrationEvent, PaymentExpiredIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.payment-expired";
                options.BindingKeys = [PaymentExpiredIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<PaymentFailedIntegrationEvent, PaymentFailedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.payment-failed";
                options.BindingKeys = [PaymentFailedIntegrationEvent.EventType];
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
            services.AddVietRideEventConsumer<TripCompletedIntegrationEvent, TripCompletedIntegrationEventHandler>(options =>
            {
                options.QueueName = "booking.trip-completed";
                options.BindingKeys = [TripCompletedIntegrationEvent.EventType];
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

    /// <summary>
    /// Creates the Identity phone-lookup retry policy. Unlike the shared legacy policy,
    /// this deliberately excludes HTTP 408 and every other 4xx response.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> CreateIdentityUserRetryPolicy(
        int retryCount = HttpResiliencePolicies.DefaultRetryCount,
        Func<int, TimeSpan>? delayProvider = null)
    {
        delayProvider ??= GetIdentityUserRetryDelay;

        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(response => (int)response.StatusCode >= 500)
            .WaitAndRetryAsync(retryCount, delayProvider);
    }

    public static TimeSpan GetIdentityUserRetryDelay(int attempt)
        => attempt switch
        {
            1 => TimeSpan.FromMilliseconds(200),
            2 => TimeSpan.FromMilliseconds(500),
            3 => TimeSpan.FromSeconds(1),
            _ => throw new ArgumentOutOfRangeException(
                nameof(attempt),
                attempt,
                "Identity user retry attempts must be between 1 and 3."),
        };

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

    private static string ResolveParcelBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration["Parcel:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("PARCEL_SERVICE_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Parcel base URL must be configured via Parcel:BaseUrl or PARCEL_SERVICE_BASE_URL.");
        }

        return baseUrl;
    }

    private static void ConfigurePlatformReportClient(HttpClient client, string baseUrl)
    {
        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }
}
