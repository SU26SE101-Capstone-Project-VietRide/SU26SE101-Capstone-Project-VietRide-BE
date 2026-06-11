using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Shared.Persistence.DependencyInjection;

/// One-call EF Core registration for each VietRide service.
/// Wires Npgsql provider + Npgsql retry policy + outbox store.
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddVietRideDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Default",
        Action<NpgsqlDataSourceBuilder>? configureDataSource = null)
        where TContext : VietRideDbContextBase
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{connectionStringName} is not configured.");

        services.AddSingleton(_ =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            ConfigureSharedPostgresTypes(dataSourceBuilder);
            configureDataSource?.Invoke(dataSourceBuilder);
            return dataSourceBuilder.Build();
        });

        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), ConfigureNpgsqlRetry);
        });

        // Expose the concrete context as VietRideDbContextBase for shared services (e.g. OutboxStore).
        services.AddScoped<VietRideDbContextBase>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddScoped<IIntegrationEventOutbox, IntegrationEventOutbox>();

        // Wire the shared IUnitOfWork implementation backed by VietRideDbContextBase.
        // Resolves EfUnitOfWork via the already-registered VietRideDbContextBase alias,
        // keeping it service-agnostic (no TContext dependency in EfUnitOfWork).
        services.AddScoped<IUnitOfWork>(sp =>
            new EfUnitOfWork(sp.GetRequiredService<VietRideDbContextBase>()));

        return services;
    }

    private static void ConfigureSharedPostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        dataSourceBuilder.MapEnum<OutboxEventStatus>(
            "outbox_event_status",
            new NpgsqlNullNameTranslator());
    }

    private static void ConfigureNpgsqlRetry(
        Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }
}
