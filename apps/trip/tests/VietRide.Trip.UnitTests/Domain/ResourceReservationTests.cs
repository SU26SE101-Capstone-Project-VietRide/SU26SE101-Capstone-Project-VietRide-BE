using FluentAssertions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class ResourceReservationTests
{
    [Fact]
    public void Release_ActiveReservation_TruncatesOperationalEndAtReleaseTime()
    {
        var start = DateTimeOffset.Parse("2026-08-11T01:00:00Z");
        var reservation = Create(start, start.AddHours(2));
        reservation.Activate(start).Should().BeTrue();

        reservation.Release(start.AddMinutes(75)).Should().BeTrue();

        reservation.Status.Should().Be(ResourceReservationStatus.RELEASED);
        reservation.PlannedEndAt.Should().Be(start.AddMinutes(75));
        reservation.ReleasedAt.Should().Be(start.AddMinutes(75));
    }

    [Fact]
    public void Create_RejectsInvalidVehicleRolePair()
    {
        var start = DateTimeOffset.Parse("2026-08-11T01:00:00Z");

        var action = () => ResourceReservation.CreateForTrip(
            Guid.NewGuid(),
            ResourceReservationType.VEHICLE,
            ResourceReservationRole.DRIVER,
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            start.AddHours(1),
            null,
            null,
            10m,
            106m,
            11m,
            107m);

        action.Should().Throw<ArgumentException>();
    }

    private static ResourceReservation Create(DateTimeOffset start, DateTimeOffset end) =>
        ResourceReservation.CreateForTrip(
            Guid.NewGuid(),
            ResourceReservationType.CREW,
            ResourceReservationRole.DRIVER,
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            end,
            null,
            null,
            10m,
            106m,
            11m,
            107m);
}
