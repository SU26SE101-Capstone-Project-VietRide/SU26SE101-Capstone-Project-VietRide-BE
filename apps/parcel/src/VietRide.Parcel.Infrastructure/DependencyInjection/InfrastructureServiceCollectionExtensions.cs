using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using VietRide.Parcel.Application.Abstractions.Caching;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Infrastructure.Caching;
using VietRide.Parcel.Infrastructure.Http;
using VietRide.Parcel.Infrastructure.Jobs;
using VietRide.Parcel.Infrastructure.Messaging;
using VietRide.Parcel.Infrastructure.Persistence.Repositories;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Reporting;

namespace VietRide.Parcel.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public const string HangfireSchemaName = "hangfire";

    public static IServiceCollection AddParcelHangfire(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IExcelReportWriter, ClosedXmlExcelReportWriter>();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                connectionString,
                new PostgreSqlStorageOptions
                {
                    SchemaName = HangfireSchemaName,
                    PrepareSchemaIfNecessary = true,
                }));

        services.AddScoped<ParcelReviewTimeoutJob>();
        services.AddScoped<ParcelAdditionalPaymentTimeoutJob>();
        services.AddScoped<ParcelPendingAutoRejectJob>();
        services.AddScoped<ParcelLifecycleSweepJob>();
        services.AddScoped<ParcelDeliveryPendingConfirmReminderJob>();
        services.AddScoped<PlatformParcelStatsBackfillJob>();

        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        bool registerConsumers = true)
    {
        var redisUrl = configuration["REDIS_URL"]
            ?? Environment.GetEnvironmentVariable("REDIS_URL")
            ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisOptions));

        services.AddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.AddTransient<InternalJwtDelegatingHandler>();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        services.AddScoped<IParcelRepository, ParcelRepository>();
        services.AddScoped<SentParcelHistoryReader>();
        services.AddScoped<IParcelRouteFareRepository, ParcelRouteFareRepository>();
        services.AddScoped<IParcelStatsRepository, ParcelStatsRepository>();
        services.AddScoped<IParcelPricingPolicyRepository, ParcelPricingPolicyRepository>();
        services.AddScoped<IParcelReportCache, RedisParcelReportCache>();

        if (registerConsumers)
        {
            services.AddVietRideEventConsumer<PaymentSucceededIntegrationEvent, PaymentSucceededIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.payment-succeeded";
                options.BindingKeys = [PaymentSucceededIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<PaymentFailedIntegrationEvent, PaymentFailedIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.payment-failed";
                options.BindingKeys = [PaymentFailedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<PaymentExpiredIntegrationEvent, PaymentExpiredIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.payment-expired";
                options.BindingKeys = [PaymentExpiredIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<TripStartedIntegrationEvent, TripStartedIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.trip-started";
                options.BindingKeys = [TripStartedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<TripCompletedIntegrationEvent, TripCompletedIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.trip-completed";
                options.BindingKeys = [TripCompletedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<TripCancelledIntegrationEvent, TripCancelledIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.trip-cancelled";
                options.BindingKeys = [TripCancelledIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<TripDisruptedIntegrationEvent, TripDisruptedIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.trip-disrupted";
                options.BindingKeys = [TripDisruptedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<TripVehicleSubstitutedIntegrationEvent, TripVehicleSubstitutedIntegrationEventHandler>(options =>
            {
                options.QueueName = "parcel.trip-vehicle-substituted";
                options.BindingKeys = [TripVehicleSubstitutedIntegrationEvent.EventType];
            });
        }

        RegisterTripClient(services, configuration, hostEnvironment);
        RegisterPaymentClient(services, configuration, hostEnvironment);
        RegisterBookingClient(services, configuration, hostEnvironment);
        RegisterIdentityClient(services, configuration, hostEnvironment);

        return services;
    }

    private static void RegisterTripClient(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        if (UseDevStub(configuration, hostEnvironment, "Trip"))
        {
            services.AddScoped<ITripServiceClient, DevTripServiceClient>();
        }
        else
        {
            var baseUrl = ResolveBaseUrl(configuration, "Trip:BaseUrl", "TRIP_SERVICE_BASE_URL");
            services
                .AddHttpClient<ITripServiceClient, TripServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
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
    }

    private static void RegisterPaymentClient(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        if (UseDevStub(configuration, hostEnvironment, "Payment"))
        {
            services.AddScoped<IPaymentServiceClient, DevPaymentServiceClient>();
        }
        else
        {
            var baseUrl = ResolveBaseUrl(configuration, "Payment:BaseUrl", "PAYMENT_SERVICE_BASE_URL");
            services
                .AddHttpClient<IPaymentServiceClient, PaymentServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
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
    }

    private static void RegisterBookingClient(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        if (UseDevStub(configuration, hostEnvironment, "Booking"))
        {
            services.AddScoped<IBookingServiceClient, DevBookingServiceClient>();
        }
        else
        {
            var baseUrl = ResolveBaseUrl(configuration, "Booking:BaseUrl", "BOOKING_SERVICE_BASE_URL");
            services
                .AddHttpClient<IBookingServiceClient, BookingServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
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
    }

    private static void RegisterIdentityClient(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        if (UseDevStub(configuration, hostEnvironment, "Identity"))
        {
            services.AddScoped<IIdentityServiceClient, DevIdentityServiceClient>();
        }
        else
        {
            var baseUrl = ResolveBaseUrl(configuration, "Identity:BaseUrl", "IDENTITY_SERVICE_BASE_URL");
            services
                .AddHttpClient<IIdentityServiceClient, IdentityServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
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
    }

    private static string ResolveBaseUrl(IConfiguration configuration, string configKey, string envKey)
    {
        var baseUrl = configuration[configKey]
            ?? Environment.GetEnvironmentVariable(envKey);

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                $"Base URL must be configured via {configKey.Replace(":", "__")} or {envKey}.");
        }

        return baseUrl;
    }

    private static bool UseDevStub(IConfiguration configuration, IHostEnvironment hostEnvironment, string serviceName)
    {
        var enabled = configuration.GetValue($"{serviceName}:UseDevStub", false)
            || string.Equals(
                Environment.GetEnvironmentVariable($"{serviceName.ToUpperInvariant()}_USE_DEV_STUB"),
                "true",
                StringComparison.OrdinalIgnoreCase);

        if (enabled && hostEnvironment.IsProduction())
        {
            throw new InvalidOperationException($"{serviceName} dev stub cannot be enabled in Production.");
        }

        return enabled;
    }
}
