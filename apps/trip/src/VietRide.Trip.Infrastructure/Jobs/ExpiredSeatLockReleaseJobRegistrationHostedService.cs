using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace VietRide.Trip.Infrastructure.Jobs;

internal sealed class ExpiredSeatLockReleaseJobRegistrationHostedService : IHostedService
{
    private const string DefaultQueueName = "trip";
    private const string JobId = "trip.release-expired-seat-locks";
    private readonly IConfiguration configuration;
    private readonly IRecurringJobManager recurringJobManager;

    public ExpiredSeatLockReleaseJobRegistrationHostedService(
        IConfiguration configuration,
        IRecurringJobManager recurringJobManager)
    {
        this.configuration = configuration;
        this.recurringJobManager = recurringJobManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var queueName = configuration["Hangfire:QueueName"] ?? DefaultQueueName;
#pragma warning disable CS0618
        recurringJobManager.AddOrUpdate(
            JobId,
            Job.FromExpression<ExpiredSeatLockReleaseJob>(job => job.ReleaseExpiredAsync(CancellationToken.None)),
            Cron.Minutely(),
            new RecurringJobOptions { QueueName = queueName, TimeZone = TimeZoneInfo.Utc });
#pragma warning restore CS0618
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
