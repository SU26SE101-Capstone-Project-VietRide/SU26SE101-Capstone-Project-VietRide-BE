using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using NSubstitute;
using VietRide.Booking.Infrastructure.Jobs;

namespace VietRide.Booking.UnitTests.Jobs;

public sealed class PendingActionRealertJobTests
{
    [Fact]
    public void DeterministicOutboxIdentity_IsStablePerPendingAction()
    {
        var pendingActionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var first = PendingActionRealertJob.DeriveEventId(pendingActionId);
        var duplicate = PendingActionRealertJob.DeriveEventId(pendingActionId);
        var other = PendingActionRealertJob.DeriveEventId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        duplicate.Should().Be(first);
        other.Should().NotBe(first);
    }

    [Fact]
    public void JobIsPinnedToBookingQueue()
    {
        var attribute = typeof(PendingActionRealertJob)
            .GetMethod(nameof(PendingActionRealertJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(QueueAttribute), inherit: true)
            .Cast<QueueAttribute>()
            .Single();

        attribute.Queue.Should().Be("booking");
    }

    [Fact]
    public void SchedulerUsesExactPublisherOccurrencePlusTwoHoursAndLogicalActionKey()
    {
        var client = Substitute.For<IBackgroundJobClient>();
        var scheduler = new HangfirePendingActionRealertScheduler(client);
        var pendingActionId = Guid.NewGuid();
        var dueAt = new DateTimeOffset(2026, 7, 15, 4, 0, 0, TimeSpan.Zero);

        scheduler.EnsureScheduled(pendingActionId, dueAt);

        client.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
        var call = client.ReceivedCalls().Single(received => received.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create));
        var job = (Job)call.GetArguments()[0]!;
        var state = (ScheduledState)call.GetArguments()[1]!;
        job.Type.Should().Be(typeof(PendingActionRealertJob));
        job.Method.Name.Should().Be(nameof(PendingActionRealertJob.ExecuteAsync));
        job.Args[0].Should().Be(pendingActionId);
        state.EnqueueAt.Should().Be(dueAt.UtcDateTime);
    }
}
