using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Infrastructure.ExternalClients;
using VietRide.Trip.Infrastructure.Persistence.Repositories;

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
        IConfiguration configuration)
    {
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IOperatorStationRepository, OperatorStationRepository>();
        services.AddScoped<IStopRepository, StopRepository>();
        services.AddScoped<IRouteRepository, RouteRepository>();
        services.AddScoped<IRouteStopRepository, RouteStopRepository>();
        services.AddScoped<IRouteStopFareTemplateRepository, RouteStopFareTemplateRepository>();
        services.AddScoped<IAlternativeRouteRepository, AlternativeRouteRepository>();
        services.AddScoped<IVehicleTypeRepository, VehicleTypeRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();

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
