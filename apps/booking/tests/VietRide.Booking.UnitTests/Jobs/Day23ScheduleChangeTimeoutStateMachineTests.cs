using FluentAssertions;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.Services;

namespace VietRide.Booking.UnitTests.Jobs;

public sealed class Day23ScheduleChangeTimeoutStateMachineTests
{
    private static readonly DateTimeOffset Initial = DateTimeOffset.Parse("2026-07-17T10:00:00Z");

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Medium_AutoAcceptsOnlyStrictlyAfterInitialCutoff(long ticks, bool expected)
    {
        var action = CreateAction(BookingPendingActionSeverity.MEDIUM, Initial);

        ScheduleChangeResolutionStateMachine.IsAutoAcceptDue(
            action, Initial, null, Initial.AddTicks(ticks)).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(35999999999, true, false)]
    [InlineData(36000000000, true, false)]
    [InlineData(36000000001, false, true)]
    public void Major_WithLaterTerminal_SeparatesInitialAndFinalPhases(
        long ticks,
        bool initialPhase,
        bool finalPhase)
    {
        var terminal = Initial.AddHours(1);
        var action = CreateAction(BookingPendingActionSeverity.MAJOR, Initial);
        var now = Initial.AddTicks(ticks);

        ScheduleChangeResolutionStateMachine.IsMajorInitialPhaseDue(
            action, Initial, terminal, now).Should().Be(initialPhase);
        ScheduleChangeResolutionStateMachine.IsAutoAcceptDue(
            action, Initial, terminal, now).Should().Be(finalPhase);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Major_WithNonLaterTerminal_SkipsInitialPhaseAndUsesInitialCutoff(long ticks, bool finalPhase)
    {
        var action = CreateAction(BookingPendingActionSeverity.MAJOR, Initial);
        var terminal = Initial.AddMinutes(-1);
        var now = Initial.AddTicks(ticks);

        ScheduleChangeResolutionStateMachine.IsMajorInitialPhaseDue(
            action, Initial, terminal, now).Should().BeFalse();
        ScheduleChangeResolutionStateMachine.IsAutoAcceptDue(
            action, Initial, terminal, now).Should().Be(finalPhase);
    }

    private static BookingPendingAction CreateAction(
        BookingPendingActionSeverity severity,
        DateTimeOffset deadline)
        => BookingPendingAction.Create(
            Guid.NewGuid(),
            BookingPendingActionReason.SCHEDULE_CHANGE,
            deadline,
            severity,
            "{}");
}
