using FluentAssertions;
using VietRide.Payment.Application.Features.Settlements;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.UnitTests.Features.Settlements;

public sealed class TripSettlementScheduleTests
{
    [Fact]
    public void Schedule_ExposesExistingUtcCronCadence()
    {
        TripSettlementSchedule.HoldDays.Should().Be(7);
        TripSettlementSchedule.EligibilityCron.Should().Be("0 19 * * *");
        TripSettlementSchedule.AutoSettlementCron.Should().Be("0 2 * * 1");
        TripSettlementSchedule.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void PendingHold_AfterSundaySweep_WaitsForNextEligibilitySweepAndFollowingMonday()
    {
        var now = new DateTimeOffset(2026, 8, 9, 19, 30, 0, TimeSpan.Zero);
        var eligibleAt = new DateTimeOffset(2026, 8, 9, 20, 0, 0, TimeSpan.Zero);

        var next = TripSettlementSchedule.GetNextScheduledAttemptAt(
            OperatorTripSettlementStatus.PENDING_HOLD,
            eligibleAt,
            now);

        next.Should().Be(new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Eligible_AfterFailedMondayAttempt_RetriesOnFollowingMonday()
    {
        var now = new DateTimeOffset(2026, 8, 10, 2, 0, 0, TimeSpan.Zero);

        TripSettlementSchedule.GetNextScheduledAttemptAt(
                OperatorTripSettlementStatus.ELIGIBLE,
                now.AddDays(-1),
                now)
            .Should().Be(new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero));
    }
}
