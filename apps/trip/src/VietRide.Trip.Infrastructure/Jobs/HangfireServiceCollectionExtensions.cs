using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VietRide.Trip.Infrastructure.Jobs;

/// <summary>
/// Registers Hangfire for Trip-owned business scheduled jobs.
/// </summary>
public static class HangfireServiceCollectionExtensions
{
    private const string DefaultSchemaName = "hangfire";
    private const string DefaultQueueName = "trip";

    /// <summary>
    /// Adds Hangfire storage and a Trip queue worker backed by the Trip PostgreSQL database.
    /// </summary>
    public static IServiceCollection AddTripHangfire(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Trip Hangfire requires ConnectionStrings:Default to point at the vietride_trip database.");
        }

        var schemaName = configuration["Hangfire:SchemaName"] ?? DefaultSchemaName;
        var queueName = configuration["Hangfire:QueueName"] ?? DefaultQueueName;
        var workerCount = configuration.GetValue<int?>("Hangfire:WorkerCount") ?? 2;

        services.AddHangfire(globalConfiguration => globalConfiguration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                connectionString,
                new PostgreSqlStorageOptions
                {
                    SchemaName = schemaName
                }));

        services.AddHangfireServer(options =>
        {
            options.ServerName = "vietride-trip";
            options.Queues = [queueName];
            options.WorkerCount = workerCount;
        });
        services.AddHostedService<ExpiredSeatLockReleaseJobRegistrationHostedService>();

        return services;
    }
}
