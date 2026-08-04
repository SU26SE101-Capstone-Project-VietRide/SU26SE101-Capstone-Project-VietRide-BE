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
}
