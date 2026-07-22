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
        Action<NpgsqlDataSourceBuilder>? configureDataSource = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
        where TContext : VietRideDbContextBase
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{connectionStringName} is not configured.");
        var schemaName = typeof(TContext).GetField("SchemaName")?.GetRawConstantValue() as string;

        services.AddSingleton(_ =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            ConfigureSharedPostgresTypes(dataSourceBuilder, schemaName);
            configureDataSource?.Invoke(dataSourceBuilder);
            return dataSourceBuilder.Build();
        });

        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql =>
            {
                ConfigureNpgsqlRetry(npgsql, configuration);
                if (!string.IsNullOrWhiteSpace(schemaName))
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", schemaName);
                }
            });
            configureDbContext?.Invoke(options);
        });

        // Expose the concrete context as VietRideDbContextBase for shared services (e.g. OutboxStore).
        services.AddScoped<VietRideDbContextBase>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddScoped<IOutboxDlqReader, OutboxDlqReader>();
        services.AddScoped<IIntegrationEventOutbox, IntegrationEventOutbox>();

        // Wire the shared IUnitOfWork implementation backed by VietRideDbContextBase.
        // Resolves EfUnitOfWork via the already-registered VietRideDbContextBase alias,
        // keeping it service-agnostic (no TContext dependency in EfUnitOfWork).
        services.AddScoped<IUnitOfWork>(sp =>
            new EfUnitOfWork(sp.GetRequiredService<VietRideDbContextBase>()));

        return services;
    }

    private static void ConfigureSharedPostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder, string? schemaName)
    {
        var outboxEventStatusTypeName = string.IsNullOrWhiteSpace(schemaName)
            ? "outbox_event_status"
            : $"{schemaName}.outbox_event_status";

        dataSourceBuilder.MapEnum<OutboxEventStatus>(
            outboxEventStatusTypeName,
            new NpgsqlNullNameTranslator());
    }

    private static void ConfigureNpgsqlRetry(
        Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql,
        IConfiguration configuration)
    {
        var enableRetry = bool.TryParse(
            configuration["Database:EnableRetryOnFailure"],
            out var configuredEnableRetry)
            && configuredEnableRetry;
        if (!enableRetry)
        {
            return;
        }

        npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }
}
