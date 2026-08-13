using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Hosting;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class TripLifecycleJobRegistrationHostedService : IHostedService
{
    public const string AutoBoardingJobId = "trip.auto-boarding";
    public const string AutoStartFallbackJobId = "trip.auto-start-fallback";
    public const string AutoCompletedFallbackJobId = "trip.auto-completed-fallback";
    public const string EveryMinuteCron = "* * * * *";
    public const string EveryFifteenMinutesCron = "*/15 * * * *";
    public const string EveryFiveMinutesCron = "*/5 * * * *";
    private const string QueueName = "trip";

    private readonly IRecurringJobManager recurringJobManager;

    public TripLifecycleJobRegistrationHostedService(IRecurringJobManager recurringJobManager)
    {
        this.recurringJobManager = recurringJobManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Register<AutoBoardingJob>(
            AutoBoardingJobId,
            job => job.ScanAsync(CancellationToken.None),
            EveryMinuteCron);
        Register<AutoStartFallbackJob>(
            AutoStartFallbackJobId,
            job => job.ScanAsync(CancellationToken.None),
            EveryFiveMinutesCron);
        Register<AutoCompletedFallbackJob>(
            AutoCompletedFallbackJobId,
            job => job.ScanAsync(CancellationToken.None),
            EveryFifteenMinutesCron);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Register<TJob>(
        string jobId,
        System.Linq.Expressions.Expression<Func<TJob, Task>> methodCall,
        string cron)
    {
#pragma warning disable CS0618
        recurringJobManager.AddOrUpdate(
            jobId,
            Job.FromExpression(methodCall),
            cron,
            new RecurringJobOptions
            {
                QueueName = QueueName,
                TimeZone = TimeZoneInfo.Utc,
            });
#pragma warning restore CS0618
    }
}
