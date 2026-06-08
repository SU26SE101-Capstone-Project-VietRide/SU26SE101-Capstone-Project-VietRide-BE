using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
