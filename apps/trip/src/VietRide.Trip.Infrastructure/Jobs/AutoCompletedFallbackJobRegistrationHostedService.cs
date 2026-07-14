using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Hosting;

namespace VietRide.Trip.Infrastructure.Jobs;

internal sealed class AutoCompletedFallbackJobRegistrationHostedService : IHostedService
{
    private const string JobId = "trip.auto-complete-fallback";
    private readonly IRecurringJobManager _jobs;

    public AutoCompletedFallbackJobRegistrationHostedService(IRecurringJobManager jobs)
    {
        _jobs = jobs;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
#pragma warning disable CS0618 // Hangfire 1.8 IRecurringJobManager lacks the explicit queue overload.
        _jobs.AddOrUpdate(
            JobId,
            Job.FromExpression<AutoCompletedFallbackJob>(job =>
                job.RunAsync(CancellationToken.None)),
            Cron.Minutely(),
            new RecurringJobOptions { QueueName = "trip", TimeZone = TimeZoneInfo.Utc });
#pragma warning restore CS0618
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
