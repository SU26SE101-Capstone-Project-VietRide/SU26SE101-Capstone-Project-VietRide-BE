using FluentAssertions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class ShuttleDomainTests
{
    [Fact]
    public void CreateShuttleTrip_WithInvalidSchedule_Throws()
    {
        var departure = DateTimeOffset.UtcNow;

        var action = () => ShuttleTrip.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            departure, departure, null);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignPassenger_ChangesPendingStateAndCannotBeRepeated()
    {
        var passenger = ShuttlePassenger.Request(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "123 Nguyen Hue", 10.77m, 106.70m);
        var shuttleTripId = Guid.NewGuid();

        passenger.Assign(shuttleTripId, 2);

        passenger.ShuttleTripId.Should().Be(shuttleTripId);
        passenger.PickupOrder.Should().Be(2);
        passenger.Status.Should().Be(ShuttlePassenger.PendingStatus);
        FluentActions.Invoking(() => passenger.Assign(Guid.NewGuid(), 3))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CancelPassenger_IsIdempotent()
    {
        var passenger = ShuttlePassenger.Request(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "123 Nguyen Hue", 10.77m, 106.70m);

        passenger.Cancel("AUTO_UNFULFILLED_CUTOFF");
        passenger.Cancel("OTHER");

        passenger.Status.Should().Be(ShuttlePassenger.CancelledStatus);
        passenger.CancelReason.Should().Be("AUTO_UNFULFILLED_CUTOFF");
    }
}
