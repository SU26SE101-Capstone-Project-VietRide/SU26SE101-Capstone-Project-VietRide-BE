using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Jobs;

namespace VietRide.Booking.Infrastructure.Jobs;

public static class HangfireServiceCollectionExtensions
{
    private const string DefaultSchemaName = "hangfire";
    private const string DefaultQueueName = "booking";

    public static IServiceCollection AddBookingHangfire(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Booking Hangfire requires ConnectionStrings:Default to point at the vietride_booking database.");
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
                    SchemaName = schemaName,
                    PrepareSchemaIfNecessary = true,
                }));

        services.AddHangfireServer(options =>
        {
            options.ServerName = "vietride-booking";
            options.Queues = [queueName];
            options.WorkerCount = workerCount;
        });
        services.AddScoped<IPendingActionRealertScheduler, HangfirePendingActionRealertScheduler>();
        services.AddScoped<PlatformBookingStatsBackfillJob>();

        return services;
    }
}
