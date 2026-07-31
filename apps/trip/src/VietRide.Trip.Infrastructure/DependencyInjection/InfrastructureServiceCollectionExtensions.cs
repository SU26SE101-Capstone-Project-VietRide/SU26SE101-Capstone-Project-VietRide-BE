using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Application.Security;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Reporting;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Infrastructure.ExternalClients;
using VietRide.Trip.Infrastructure.Http;
using VietRide.Trip.Infrastructure.Jobs;
using VietRide.Trip.Infrastructure.Messaging;
using VietRide.Trip.Infrastructure.Persistence.Repositories;
using VietRide.Trip.Infrastructure.SeatLock;
using VietRide.Trip.Infrastructure.SeatLocks;
using VietRide.Trip.Infrastructure.Services;

namespace VietRide.Trip.Infrastructure.DependencyInjection;

/// <summary>
/// Registers Trip Infrastructure services such as repositories and external clients.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds Trip Infrastructure services to the DI container.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool backgroundWorkersEnabled)
    {
        services.Configure<BookingImpactClientOptions>(
            configuration.GetSection(BookingImpactClientOptions.SectionName));
        services.AddSingleton<IExcelReportWriter, ClosedXmlExcelReportWriter>();
        services.AddSingleton<IFirebaseStorageImageUrlValidator>(_ =>
            new FirebaseStorageImageUrlValidator(
                configuration["FIREBASE_WEB_STORAGE_BUCKET"]
                ?? configuration["FIREBASE_STORAGE_BUCKET"]));
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IOperatorStationRepository, OperatorStationRepository>();
        services.AddScoped<IStopRepository, StopRepository>();
        services.AddScoped<IRouteRepository, RouteRepository>();
        services.AddScoped<IRouteStopRepository, RouteStopRepository>();
        services.AddScoped<IRouteStopFareTemplateRepository, RouteStopFareTemplateRepository>();
        services.AddScoped<IAlternativeRouteRepository, AlternativeRouteRepository>();
        services.AddScoped<IVehicleTypeRepository, VehicleTypeRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IOperatorAnalyticsRepository, OperatorAnalyticsRepository>();
        services.AddScoped<IDriverScheduleRepository, DriverScheduleRepository>();
        services.AddScoped<IDriverScheduleAuditLogRepository, DriverScheduleAuditLogRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<ITripAuditLogRepository, TripAuditLogRepository>();
        services.AddScoped<ITripVehicleSwapService, TripVehicleSwapService>();
        services.AddScoped<IRoundTripSeatLockStore, RedisRoundTripSeatLockStore>();
        services.AddScoped<ITripSeatRepository, TripSeatRepository>();
        services.AddScoped<ITripStopRepository, TripStopRepository>();
        services.AddScoped<ITripStopFareRepository, TripStopFareRepository>();
        services.AddScoped<ITripGenerationSkipLogRepository, TripGenerationSkipLogRepository>();
        services.AddScoped<IIncidentRepository, IncidentRepository>();
        if (backgroundWorkersEnabled)
        {
            services.AddScoped<ITripGenerationJobScheduler, HangfireTripGenerationJobScheduler>();
        }
        else
        {
            services.AddScoped<ITripGenerationJobScheduler, DisabledTripGenerationJobScheduler>();
        }
        services.AddScoped<IShuttleDispatchService, ShuttleDispatchService>();
        services.AddScoped<ShuttleDispatchSafetyJob>();
        services.AddScoped<AutoBoardingJob>();
        services.AddScoped<AutoStartFallbackJob>();
        services.AddScoped<AutoCompletedFallbackJob>();
        services.AddScoped<PlatformTripStatsBackfillJob>();
        if (backgroundWorkersEnabled)
        {
            services.AddVietRideEventConsumer<BookingShuttleConfirmedIntegrationEvent, BookingShuttleConfirmedIntegrationEventHandler>(options =>
            {
                options.QueueName = "trip.booking-shuttle-confirmed";
                options.BindingKeys = [BookingShuttleConfirmedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<BookingShuttleCancelledIntegrationEvent, BookingShuttleCancelledIntegrationEventHandler>(options =>
            {
                options.QueueName = "trip.booking-shuttle-cancelled";
                options.BindingKeys = [BookingShuttleCancelledIntegrationEvent.EventType];
            });
            services.AddHostedService<TripGenerationRecurringJobRegistrationHostedService>();
            services.AddHostedService<ShuttleDispatchSafetyJobRegistrationHostedService>();
            services.AddHostedService<TripLifecycleJobRegistrationHostedService>();
            services.AddHostedService<PlatformTripStatsBackfillJobRegistrationHostedService>();
        }

        var redisUrl = configuration["REDIS_URL"]
            ?? Environment.GetEnvironmentVariable("REDIS_URL")
            ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<ISeatLockTtlProvider, SeatLockTtlProvider>();
        services.AddSingleton<ISeatLockStore, RedisSeatLockStore>();
        services.AddSingleton<ISeatLockIdempotencyStore, RedisSeatLockIdempotencyStore>();
        services.AddScoped<IExpiredSeatLockReleaser, ExpiredSeatLockReleaser>();

        services.AddTripHangfire(configuration, backgroundWorkersEnabled);

        services.AddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.AddTransient<InternalJwtDelegatingHandler>();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        services
            .AddHttpClient<IIdentityInternalClient, IdentityInternalClient>(client =>
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
        services.AddScoped<ISubscriptionQuotaClient>(serviceProvider =>
            (ISubscriptionQuotaClient)serviceProvider.GetRequiredService<IIdentityInternalClient>());
        services.AddHttpClient<IBookingImpactClient, VietRide.Trip.Infrastructure.Http.BookingImpactClient>(client =>
            {
                client.BaseAddress = new Uri(configuration["BOOKING_BASE_URL"] ?? "http://booking:5003", UriKind.Absolute);
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>();
        services.AddHttpClient<IParcelImpactClient, ParcelImpactClient>(client =>
            {
                client.BaseAddress = new Uri(
                    configuration["PARCEL_BASE_URL"] ?? "http://parcel:5005",
                    UriKind.Absolute);
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>();

        return services;
    }

    private static string ResolveIdentityBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration["Identity:BaseUrl"]
            ?? configuration["IdentityService:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("IDENTITY_SERVICE_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Identity base URL must be configured via Identity:BaseUrl or IDENTITY_SERVICE_BASE_URL.");
        }

        return baseUrl;
    }

}
