using FluentAssertions;
using Hangfire;
using NSubstitute;
using VietRide.Booking.Infrastructure.Jobs;

namespace VietRide.Booking.UnitTests.Jobs;

public sealed class Day23RealertScheduleSeparationTests
{
    [Fact]
    public void PhaseIdentitiesAreDeterministicAndDistinctFromDay22()
    {
        var actionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        ScheduleChangeAutoAcceptJob.DeriveMajorInitialPhaseEventId(actionId).Should()
            .Be(ScheduleChangeAutoAcceptJob.DeriveMajorInitialPhaseEventId(actionId));
        ScheduleChangeAutoAcceptJob.DeriveMajorInitialPhaseEventId(actionId).Should()
            .NotBe(PendingActionRealertJob.DeriveEventId(actionId));
        ScheduleChangeAutoAcceptJob.DeriveAutoResolvedEventId(actionId).Should()
            .NotBe(ScheduleChangeAutoAcceptJob.DeriveMajorInitialPhaseEventId(actionId));
    }

    [Fact]
    public void SchedulerTargetsSeparateJobAtExactCutoffPlusOneSecond()
    {
        var client = Substitute.For<IBackgroundJobClient>();
        var scheduler = new HangfireScheduleChangeAutoAcceptScheduler(client);
        var actionId = Guid.NewGuid();
        var scheduledAt = DateTimeOffset.Parse("2026-07-17T10:00:01Z");

        scheduler.EnsureScheduled(actionId, scheduledAt);

        client.Received(1).Create(
            Arg.Is<Hangfire.Common.Job>(job =>
                job.Type == typeof(ScheduleChangeAutoAcceptJob)
                && job.Method.Name == nameof(ScheduleChangeAutoAcceptJob.ExecuteAsync)
                && (Guid)job.Args[0] == actionId),
            Arg.Is<Hangfire.States.ScheduledState>(state => state.EnqueueAt == scheduledAt.UtcDateTime));
    }
}
