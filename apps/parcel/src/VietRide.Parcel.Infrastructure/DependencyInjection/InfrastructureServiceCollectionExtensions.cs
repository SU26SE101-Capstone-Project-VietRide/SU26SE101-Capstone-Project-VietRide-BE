using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;
using VietRide.Parcel.Infrastructure.Messaging;
using VietRide.Parcel.Infrastructure.Persistence.Repositories;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;

namespace VietRide.Parcel.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
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
        services.AddScoped<IParcelRouteFareRepository, ParcelRouteFareRepository>();
        services.AddScoped<IParcelStatsRepository, ParcelStatsRepository>();

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
        }

        RegisterTripClient(services, configuration);
        RegisterPaymentClient(services, configuration);
        RegisterBookingClient(services, configuration);
        RegisterIdentityClient(services, configuration);

        return services;
    }

    private static void RegisterTripClient(IServiceCollection services, IConfiguration configuration)
    {
        if (UseDevStub(configuration, "Trip"))
        {
            services.AddScoped<ITripServiceClient, DevTripServiceClient>();
        }
        else
        {
            services
                .AddHttpClient<ITripServiceClient, TripServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(ResolveBaseUrl(configuration,
                        "Trip:BaseUrl", "TRIP_SERVICE_BASE_URL"));
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

    private static void RegisterPaymentClient(IServiceCollection services, IConfiguration configuration)
    {
        if (UseDevStub(configuration, "Payment"))
        {
            services.AddScoped<IPaymentServiceClient, DevPaymentServiceClient>();
        }
        else
        {
            services
                .AddHttpClient<IPaymentServiceClient, PaymentServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(ResolveBaseUrl(configuration,
                        "Payment:BaseUrl", "PAYMENT_SERVICE_BASE_URL"));
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

    private static void RegisterBookingClient(IServiceCollection services, IConfiguration configuration)
    {
        if (UseDevStub(configuration, "Booking"))
        {
            services.AddScoped<IBookingServiceClient, DevBookingServiceClient>();
        }
        else
        {
            services
                .AddHttpClient<IBookingServiceClient, BookingServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(ResolveBaseUrl(configuration,
                        "Booking:BaseUrl", "BOOKING_SERVICE_BASE_URL"));
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

    private static void RegisterIdentityClient(IServiceCollection services, IConfiguration configuration)
    {
        if (UseDevStub(configuration, "Identity"))
        {
            services.AddScoped<IIdentityServiceClient, DevIdentityServiceClient>();
        }
        else
        {
            services
                .AddHttpClient<IIdentityServiceClient, IdentityServiceClient>(client =>
                {
                    client.BaseAddress = new Uri(ResolveBaseUrl(configuration,
                        "Identity:BaseUrl", "IDENTITY_SERVICE_BASE_URL"));
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

    private static bool UseDevStub(IConfiguration configuration, string serviceName)
        => configuration.GetValue($"{serviceName}:UseDevStub", false)
            || string.Equals(
                Environment.GetEnvironmentVariable($"{serviceName.ToUpperInvariant()}_USE_DEV_STUB"),
                "true",
                StringComparison.OrdinalIgnoreCase);
}
