using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace VietRide.Trip.Infrastructure.Jobs;

/// <summary>
/// Registers Trip recurring Hangfire jobs when the Infrastructure host starts.
/// </summary>
public sealed class TripGenerationRecurringJobRegistrationHostedService : IHostedService
{
    private const string DefaultQueueName = "trip";
    private const string GenerateActiveSchedulesJobId = "trip.generate-active-schedules";
    private const string WeeklySundayAt16UtcCron = "0 16 * * 0";
    private const string EveryFiveMinutesCron = "*/5 * * * *";

    private readonly IConfiguration configuration;
    private readonly IRecurringJobManager recurringJobManager;

    public TripGenerationRecurringJobRegistrationHostedService(
        IConfiguration configuration,
        IRecurringJobManager recurringJobManager)
    {
        this.configuration = configuration;
        this.recurringJobManager = recurringJobManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var queueName = configuration["Hangfire:QueueName"] ?? DefaultQueueName;

#pragma warning disable CS0618 // Hangfire 1.8 IRecurringJobManager lacks the explicit queue overload available on RecurringJob.
        recurringJobManager.AddOrUpdate(
            GenerateActiveSchedulesJobId,
            Job.FromExpression<TripGenerationJob>(job => job.GenerateForActiveSchedulesAsync(CancellationToken.None)),
            WeeklySundayAt16UtcCron,
            new RecurringJobOptions
            {
                QueueName = queueName,
                TimeZone = TimeZoneInfo.Utc
            });
        recurringJobManager.AddOrUpdate(
            TripBusinessCodeBackfillJob.RecurringJobId,
            Job.FromExpression<TripBusinessCodeBackfillJob>(job => job.RunAsync(CancellationToken.None)),
            EveryFiveMinutesCron,
            new RecurringJobOptions
            {
                QueueName = queueName,
                TimeZone = TimeZoneInfo.Utc
            });
#pragma warning restore CS0618

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

}
