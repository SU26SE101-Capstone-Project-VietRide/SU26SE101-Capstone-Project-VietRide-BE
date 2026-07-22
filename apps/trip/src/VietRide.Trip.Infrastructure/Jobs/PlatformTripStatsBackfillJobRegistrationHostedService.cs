using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Hosting;

namespace VietRide.Trip.Infrastructure.Jobs;

internal sealed class PlatformTripStatsBackfillJobRegistrationHostedService : IHostedService
{
    private readonly IRecurringJobManager _recurringJobs;

    public PlatformTripStatsBackfillJobRegistrationHostedService(IRecurringJobManager recurringJobs)
    {
        _recurringJobs = recurringJobs;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
        _recurringJobs.AddOrUpdate(
            PlatformTripStatsBackfillJob.RecurringJobId,
            Job.FromExpression<PlatformTripStatsBackfillJob>(job =>
                job.RunAsync(CancellationToken.None)),
            "*/5 * * * *",
            new RecurringJobOptions { QueueName = "trip", TimeZone = TimeZoneInfo.Utc });
#pragma warning restore CS0618
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
