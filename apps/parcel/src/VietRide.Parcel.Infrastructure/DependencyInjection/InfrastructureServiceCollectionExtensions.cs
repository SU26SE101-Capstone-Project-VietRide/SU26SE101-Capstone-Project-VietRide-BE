using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Infrastructure.Http;
using VietRide.Parcel.Infrastructure.Persistence.Repositories;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Kernel.Abstractions;

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

        return services;
    }
}
