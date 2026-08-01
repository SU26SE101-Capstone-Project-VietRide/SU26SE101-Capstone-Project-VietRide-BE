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
        var queueName = GetQueueName(configuration);
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
        services.AddScoped<BuyerSnapshotBackfillJob>();
        services.AddScoped<IScheduleChangeAutoAcceptScheduler, HangfireScheduleChangeAutoAcceptScheduler>();
        services.AddScoped<IRouteChangeExpiryScheduler, HangfireRouteChangeExpiryScheduler>();
        services.AddSingleton<IStopDisabledAutoFallbackScheduler, HangfireStopDisabledAutoFallbackScheduler>();
        services.AddSingleton<INoShowDetectionScheduler, HangfireNoShowDetectionScheduler>();

        return services;
    }

    public static string GetQueueName(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration["Hangfire:QueueName"];
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultQueueName
            : configured.Trim();
    }

    private sealed class HangfireStopDisabledAutoFallbackScheduler(IRecurringJobManager jobs)
        : IStopDisabledAutoFallbackScheduler
    {
        public void EnsureScheduled()
            => jobs.AddOrUpdate<StopDisabledAutoFallbackJob>(
                "booking-stop-disabled-auto-fallback",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.MinuteInterval(5),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    private sealed class HangfireNoShowDetectionScheduler(IRecurringJobManager jobs)
        : INoShowDetectionScheduler
    {
        public void EnsureScheduled()
            => jobs.AddOrUpdate<NoShowDetectionJob>(
                "booking-passenger-no-show-detection",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.MinuteInterval(5),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
