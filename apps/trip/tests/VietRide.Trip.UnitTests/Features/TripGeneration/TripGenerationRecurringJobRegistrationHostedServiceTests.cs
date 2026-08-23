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

        recurringJobs.Registrations.Should().HaveCount(2);
        var tripGeneration = recurringJobs.Registrations.Should().ContainSingle(item =>
            item.RecurringJobId == "trip.generate-active-schedules").Subject;
        tripGeneration.CronExpression.Should().Be("0 16 * * 0");
        tripGeneration.Options.Should().NotBeNull();
#pragma warning disable CS0618 // Hangfire 1.8 IRecurringJobManager stores queue through RecurringJobOptions.
        tripGeneration.Options.QueueName.Should().Be("trip");
#pragma warning restore CS0618
        tripGeneration.Options.TimeZone.Should().Be(TimeZoneInfo.Utc);
        tripGeneration.Job.Type.Should().Be(typeof(TripGenerationJob));
        tripGeneration.Job.Method.Name.Should().Be(nameof(TripGenerationJob.GenerateForActiveSchedulesAsync));

        var backfill = recurringJobs.Registrations.Should().ContainSingle(item =>
            item.RecurringJobId == TripBusinessCodeBackfillJob.RecurringJobId).Subject;
        backfill.CronExpression.Should().Be("*/5 * * * *");
        backfill.Options.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    private sealed class CapturingRecurringJobManager : IRecurringJobManager
    {
        public List<Registration> Registrations { get; } = [];

        public void AddOrUpdate(
            string recurringJobId,
            Job job,
            string cronExpression,
            RecurringJobOptions options)
        {
            Registrations.Add(new Registration(recurringJobId, job, cronExpression, options));
        }

        public void Trigger(string recurringJobId)
        {
        }

        public void RemoveIfExists(string recurringJobId)
        {
        }
    }

    private sealed record Registration(
        string RecurringJobId,
        Job Job,
        string CronExpression,
        RecurringJobOptions Options);
}
