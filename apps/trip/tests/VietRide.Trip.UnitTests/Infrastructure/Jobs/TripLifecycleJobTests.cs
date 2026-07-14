using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using VietRide.Trip.Infrastructure.Jobs;

namespace VietRide.Trip.UnitTests.Infrastructure.Jobs;

public sealed class TripLifecycleJobTests
{
    [Fact]
    public async Task Registration_UsesStableIdsUtcSchedulesAndTripQueue()
    {
        var manager = new RecordingRecurringJobManager();
        var service = new TripLifecycleJobRegistrationHostedService(manager);

        await service.StartAsync(CancellationToken.None);

        var registrations = manager.Registrations;

        registrations.Select(item => (item.Id, item.Cron)).Should().BeEquivalentTo(
            [
                (TripLifecycleJobRegistrationHostedService.AutoBoardingJobId, TripLifecycleJobRegistrationHostedService.EveryFifteenMinutesCron),
                (TripLifecycleJobRegistrationHostedService.AutoStartFallbackJobId, TripLifecycleJobRegistrationHostedService.EveryFiveMinutesCron),
                (TripLifecycleJobRegistrationHostedService.AutoCompletedFallbackJobId, TripLifecycleJobRegistrationHostedService.EveryFifteenMinutesCron),
            ]);
#pragma warning disable CS0618
        registrations.Should().OnlyContain(item =>
            item.Options.QueueName == "trip" && item.Options.TimeZone == TimeZoneInfo.Utc);
#pragma warning restore CS0618
        registrations.Select(item => item.Job.Type).Should().BeEquivalentTo(
            [typeof(AutoBoardingJob), typeof(AutoStartFallbackJob), typeof(AutoCompletedFallbackJob)]);
    }

    [Theory]
    [InlineData(typeof(AutoBoardingJob), 900)]
    [InlineData(typeof(AutoStartFallbackJob), 300)]
    [InlineData(typeof(AutoCompletedFallbackJob), 900)]
    public void Scan_UsesTripQueueAndPreventsOverlappingRuns(Type jobType, int timeoutSeconds)
    {
        var method = jobType.GetMethod("ScanAsync")!;

        method.GetCustomAttributes(typeof(QueueAttribute), inherit: false)
            .Cast<QueueAttribute>().Single().Queue.Should().Be("trip");
        method.GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: false)
            .Cast<DisableConcurrentExecutionAttribute>().Single().TimeoutSec.Should().Be(timeoutSeconds);
    }

    private sealed class RecordingRecurringJobManager : IRecurringJobManager
    {
        public List<Registration> Registrations { get; } = [];

        public void AddOrUpdate(
            string recurringJobId,
            Job job,
            string cronExpression,
            RecurringJobOptions options) =>
            Registrations.Add(new Registration(recurringJobId, job, cronExpression, options));

        public void RemoveIfExists(string recurringJobId)
        {
        }

        public void Trigger(string recurringJobId)
        {
        }
    }

    private sealed record Registration(
        string Id,
        Job Job,
        string Cron,
        RecurringJobOptions Options);
}
