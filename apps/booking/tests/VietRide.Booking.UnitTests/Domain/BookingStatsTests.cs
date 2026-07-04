using FluentAssertions;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Domain;

public class BookingStatsTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TripId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public void Create_InitializesNaturalKeyAndNullableOperatorName()
    {
        var stats = BookingStats.Create(OperatorId, new DateOnly(2026, 6, 26), TripId, "  VietRide Express  ");

        stats.OperatorId.Should().Be(OperatorId);
        stats.OperatorName.Should().Be("VietRide Express");
        stats.StatDate.Should().Be(new DateOnly(2026, 6, 26));
        stats.TripId.Should().Be(TripId);
        stats.TotalRevenue.Should().Be(Money.Zero);
        stats.TotalRefunded.Should().Be(Money.Zero);
    }

    [Fact]
    public void SetCounters_WhenCounterIsNegative_Throws()
    {
        var stats = BookingStats.Create(OperatorId, new DateOnly(2026, 6, 26), tripId: null);

        var act = () => stats.SetCounters(
            totalBookings: -1,
            totalConfirmed: 0,
            totalCancelled: 0,
            totalNoShow: 0,
            totalCompleted: 0,
            totalRevenue: Money.Zero,
            totalRefunded: Money.Zero,
            totalSeatsBooked: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
