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
    private const string DefaultTimeZoneId = "Asia/Ho_Chi_Minh";
    private const string WindowsVietnamTimeZoneId = "SE Asia Standard Time";
    private const string GenerateActiveSchedulesJobId = "trip.generate-active-schedules";
    private const string WeeklySundayAt23Cron = "0 23 * * 0";

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
        var timeZone = ResolveTimeZone(configuration["Hangfire:TripGenerationTimeZoneId"] ?? DefaultTimeZoneId);

#pragma warning disable CS0618 // Hangfire 1.8 IRecurringJobManager lacks the explicit queue overload available on RecurringJob.
        recurringJobManager.AddOrUpdate(
            GenerateActiveSchedulesJobId,
            Job.FromExpression<TripGenerationJob>(job => job.GenerateForActiveSchedulesAsync(CancellationToken.None)),
            WeeklySundayAt23Cron,
            new RecurringJobOptions
            {
                QueueName = queueName,
                TimeZone = timeZone
            });
#pragma warning restore CS0618

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (TryFindTimeZone(timeZoneId, out var configuredTimeZone))
        {
            return configuredTimeZone;
        }

        if (TryFindTimeZone(WindowsVietnamTimeZoneId, out var windowsVietnamTimeZone))
        {
            return windowsVietnamTimeZone;
        }

        return TimeZoneInfo.Utc;
    }

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZoneInfo)
    {
        try
        {
            timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZoneInfo = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZoneInfo = TimeZoneInfo.Utc;
            return false;
        }
    }
}
