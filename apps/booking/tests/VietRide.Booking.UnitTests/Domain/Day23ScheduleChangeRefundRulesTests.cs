using FluentAssertions;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.Services;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Domain;

public sealed class Day23ScheduleChangeRefundRulesTests
{
    [Theory]
    [InlineData(100_001, 50, 50_001)]
    [InlineData(100_001, 100, 100_001)]
    public void ExplicitSchedulePercentUsesAwayFromZero(long basis, int percent, long expected)
        => CancellationRefundCalculator.CalculateExplicitPercentRefund(Money.FromRaw(basis), percent)
            .Amount.Should().Be(expected);

    [Fact]
    public void EqualityAtEffectiveCutoffRemainsEligible()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var action = BookingPendingAction.Create(
            Guid.NewGuid(),
            BookingPendingActionReason.SCHEDULE_CHANGE,
            cutoff,
            BookingPendingActionSeverity.MEDIUM);

        action.ResolveScheduleChange(BookingPendingActionResolved.ACCEPTED, cutoff, cutoff);

        action.ResolvedAction.Should().Be(BookingPendingActionResolved.ACCEPTED);
    }

    [Fact]
    public void MajorUsesTerminalOnlyWhenItIsStrictlyLaterThanInitial()
    {
        var initial = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var action = BookingPendingAction.Create(
            Guid.NewGuid(),
            BookingPendingActionReason.SCHEDULE_CHANGE,
            initial,
            BookingPendingActionSeverity.MAJOR);

        ScheduleChangeResolutionStateMachine.GetEffectiveCutoff(action, initial, initial.AddHours(1))
            .Should().Be(initial.AddHours(1));
        ScheduleChangeResolutionStateMachine.GetEffectiveCutoff(action, initial, initial)
            .Should().Be(initial);
    }
}
