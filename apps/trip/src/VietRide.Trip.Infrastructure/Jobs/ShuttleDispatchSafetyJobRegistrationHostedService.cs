using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class ShuttleDispatchSafetyJobRegistrationHostedService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IRecurringJobManager _jobs;

    public ShuttleDispatchSafetyJobRegistrationHostedService(
        IConfiguration configuration,
        IRecurringJobManager jobs)
    {
        _configuration = configuration;
        _jobs = jobs;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = Math.Clamp(_configuration.GetValue("Shuttle:ScanIntervalMinutes", 5), 1, 30);
#pragma warning disable CS0618
        _jobs.AddOrUpdate(
            "trip.shuttle-dispatch-safety",
            Job.FromExpression<ShuttleDispatchSafetyJob>(job => job.ScanAsync(CancellationToken.None)),
            $"*/{interval} * * * *",
            new RecurringJobOptions { QueueName = "trip", TimeZone = TimeZoneInfo.Utc });
#pragma warning restore CS0618
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
