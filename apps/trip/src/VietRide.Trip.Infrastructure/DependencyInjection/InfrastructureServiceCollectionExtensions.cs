using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Trip.Application.Abstractions.Repositories;
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
        _ = configuration;

        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IOperatorStationRepository, OperatorStationRepository>();
        services.AddScoped<IStopRepository, StopRepository>();

        return services;
    }
}
