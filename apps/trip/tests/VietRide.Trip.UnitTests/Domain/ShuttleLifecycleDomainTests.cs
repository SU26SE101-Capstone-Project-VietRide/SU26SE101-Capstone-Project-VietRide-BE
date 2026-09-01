using FluentAssertions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class ShuttleLifecycleDomainTests
{
    [Fact]
    public void ShuttleTrip_StartAndComplete_AreIdempotentAfterTheFirstTransition()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = startedAt.AddMinutes(30);
        var shuttleTrip = ShuttleTrip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            startedAt,
            completedAt,
            null);

        shuttleTrip.Start(startedAt).Should().BeTrue();
        shuttleTrip.Start(startedAt.AddMinutes(1)).Should().BeFalse();
        shuttleTrip.Complete(completedAt).Should().BeTrue();
        shuttleTrip.Complete(completedAt.AddMinutes(1)).Should().BeFalse();
        shuttleTrip.Status.Should().Be(ShuttleTrip.CompletedStatus);
    }

    [Fact]
    public void ShuttleTrip_InvalidLifecycleTransition_ThrowsDomainException()
    {
        var now = DateTimeOffset.UtcNow;
        var shuttleTrip = ShuttleTrip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            now.AddMinutes(30),
            null);

        FluentActions.Invoking(() => shuttleTrip.Complete(now))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShuttleTrip_CreateAndCancel_PreservesNotesAndRecordsActors()
    {
        var now = DateTimeOffset.UtcNow;
        var createdByUserId = Guid.NewGuid();
        var cancelledByUserId = Guid.NewGuid();
        var shuttleTrip = ShuttleTrip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            now.AddMinutes(30),
            "Fragile luggage",
            createdByUserId: createdByUserId);

        shuttleTrip.Cancel(now.AddMinutes(5), cancelledByUserId, "Vehicle unavailable")
            .Should().BeTrue();

        shuttleTrip.CreatedByUserId.Should().Be(createdByUserId);
        shuttleTrip.CancelledAt.Should().Be(now.AddMinutes(5));
        shuttleTrip.CancelReason.Should().Be("Vehicle unavailable");
        shuttleTrip.CancelledByUserId.Should().Be(cancelledByUserId);
        shuttleTrip.Notes.Should().Be("Fragile luggage");
    }

    [Fact]
    public void ShuttleTrip_ChangeAssignment_OnlyChangesResourcesWhileScheduled()
    {
        var now = DateTimeOffset.UtcNow;
        var shuttleTrip = ShuttleTrip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            now.AddMinutes(30),
            null);
        var replacementDriverId = Guid.NewGuid();
        var replacementVehicleId = Guid.NewGuid();

        shuttleTrip.ChangeAssignment(replacementDriverId, replacementVehicleId);

        shuttleTrip.DriverUserId.Should().Be(replacementDriverId);
        shuttleTrip.VehicleId.Should().Be(replacementVehicleId);
        shuttleTrip.Start(now);
        FluentActions.Invoking(() => shuttleTrip.ChangeAssignment(Guid.NewGuid(), Guid.NewGuid()))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShuttlePassenger_DeliverRequiresPickup_AndNoShowRequiresReason()
    {
        var passenger = ShuttlePassenger.Request(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "123 Nguyen Hue",
            10.77m,
            106.70m);
        passenger.Assign(Guid.NewGuid(), 1);

        FluentActions.Invoking(() => passenger.MarkDelivered(DateTimeOffset.UtcNow))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => passenger.MarkNoShow(" "))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ShuttlePassenger_Unassign_ReturnsPendingPassengerToDispatchQueue()
    {
        var passenger = ShuttlePassenger.Request(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "123 Nguyen Hue",
            10.77m,
            106.70m);
        passenger.Assign(Guid.NewGuid(), 3, DateTimeOffset.UtcNow);

        passenger.Unassign();

        passenger.Status.Should().Be(ShuttlePassenger.PendingAssignmentStatus);
        passenger.ShuttleTripId.Should().BeNull();
        passenger.PickupOrder.Should().BeNull();
        passenger.ScheduledPickupTime.Should().BeNull();
        passenger.CancelReason.Should().BeNull();
    }

    [Fact]
    public void ShuttlePassenger_Unassign_RejectsNonPendingManifest()
    {
        var passenger = ShuttlePassenger.Request(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "123 Nguyen Hue",
            10.77m,
            106.70m);

        FluentActions.Invoking(passenger.Unassign)
            .Should().Throw<InvalidOperationException>();
    }
}
