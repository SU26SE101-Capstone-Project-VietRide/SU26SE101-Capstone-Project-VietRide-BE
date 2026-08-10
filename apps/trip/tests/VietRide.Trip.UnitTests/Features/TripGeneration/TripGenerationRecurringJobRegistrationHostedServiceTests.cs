using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Infrastructure.Jobs;

namespace VietRide.Trip.UnitTests.Features.TripGeneration;

public sealed class TripGenerationRecurringJobRegistrationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_RegistersWeeklySunday2300VietnamAsUtcTripGenerationJob()
    {
        var recurringJobs = new CapturingRecurringJobManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:QueueName"] = "trip",
                ["Hangfire:TripGenerationTimeZoneId"] = "Asia/Ho_Chi_Minh"
            })
            .Build();
        var service = new TripGenerationRecurringJobRegistrationHostedService(configuration, recurringJobs);

        await service.StartAsync(CancellationToken.None);

        recurringJobs.RecurringJobId.Should().Be("trip.generate-active-schedules");
        recurringJobs.CronExpression.Should().Be("0 16 * * 0");
        recurringJobs.Options.Should().NotBeNull();
#pragma warning disable CS0618 // Hangfire 1.8 IRecurringJobManager stores queue through RecurringJobOptions.
        recurringJobs.Options!.QueueName.Should().Be("trip");
#pragma warning restore CS0618
        recurringJobs.Options.TimeZone.Should().Be(TimeZoneInfo.Utc);
        recurringJobs.Job.Should().NotBeNull();
        recurringJobs.Job!.Type.Should().Be(typeof(TripGenerationJob));
        recurringJobs.Job.Method.Name.Should().Be(nameof(TripGenerationJob.GenerateForActiveSchedulesAsync));
    }

    private sealed class CapturingRecurringJobManager : IRecurringJobManager
    {
        public string? RecurringJobId { get; private set; }

        public Job? Job { get; private set; }

        public string? CronExpression { get; private set; }

        public RecurringJobOptions? Options { get; private set; }

        public void AddOrUpdate(
            string recurringJobId,
            Job job,
            string cronExpression,
            RecurringJobOptions options)
        {
            RecurringJobId = recurringJobId;
            Job = job;
            CronExpression = cronExpression;
            Options = options;
        }

        public void Trigger(string recurringJobId)
        {
        }

        public void RemoveIfExists(string recurringJobId)
        {
        }
    }
}
