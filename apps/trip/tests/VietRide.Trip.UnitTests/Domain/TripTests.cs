using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class TripTests
{
    [Fact]
    public void MarkBoarding_DoesNotSetActualDepartureTime()
    {
        var trip = CreateTrip();

        trip.MarkBoarding(DateTimeOffset.UtcNow);

        trip.Status.Should().Be(TripStatus.BOARDING);
        trip.ActualDepartureTime.Should().BeNull();
    }

    [Fact]
    public void Start_RequiresBoardingAndCapturesActualDepartureTime()
    {
        var trip = CreateTrip();
        var startedAt = DateTimeOffset.UtcNow;

        var action = () => trip.Start(startedAt);

        action.Should().Throw<InvalidOperationException>();
        trip.MarkBoarding(startedAt.AddMinutes(-5));
        trip.Start(startedAt);
        trip.Status.Should().Be(TripStatus.IN_PROGRESS);
        trip.ActualDepartureTime.Should().Be(startedAt);
    }

    [Fact]
    public void CompleteManually_RequiresNonEmptyActorAndInProgress()
    {
        var trip = CreateTrip();
        var now = DateTimeOffset.UtcNow;

        var beforeStart = () => trip.CompleteManually(now, Guid.NewGuid());
        beforeStart.Should().Throw<InvalidOperationException>();

        trip.MarkBoarding(now.AddMinutes(-10));
        trip.Start(now.AddMinutes(-5));
        var emptyActor = () => trip.CompleteManually(now, Guid.Empty);
        emptyActor.Should().Throw<ArgumentException>();

        var actor = Guid.NewGuid();
        trip.CompleteManually(now, actor);
        trip.CompletedByUserId.Should().Be(actor);
        trip.CompletedAt.Should().Be(now);
        trip.Status.Should().Be(TripStatus.COMPLETED);
    }

    [Fact]
    public void CompleteAutomatically_RecordsNullActorAndRequiresInProgress()
    {
        var trip = CreateTrip();
        var now = DateTimeOffset.UtcNow;

        var beforeStart = () => trip.CompleteAutomatically(now);
        beforeStart.Should().Throw<InvalidOperationException>();

        trip.MarkBoarding(now.AddMinutes(-10));
        trip.Start(now.AddMinutes(-5));
        trip.CompleteAutomatically(now);

        trip.CompletedByUserId.Should().BeNull();
        trip.CompletedAt.Should().Be(now);
        trip.Status.Should().Be(TripStatus.COMPLETED);
    }

    [Fact]
    public void ChangeRoute_WhenParentRouteChanges_ClearsAlternativeRoute()
    {
        var trip = CreateTrip();
        var newRouteId = Guid.NewGuid();
        trip.ChangeAlternativeRoute(Guid.NewGuid());

        var changed = trip.ChangeRoute(
            newRouteId,
            trip.EstimatedArrivalTime.AddMinutes(15));

        changed.Should().BeTrue();
        trip.RouteId.Should().Be(newRouteId);
        trip.AlternativeRouteId.Should().BeNull();
    }

    [Fact]
    public void ChangeRoute_WhenParentRouteIsUnchanged_PreservesAlternativeRoute()
    {
        var trip = CreateTrip();
        var alternativeRouteId = Guid.NewGuid();
        trip.ChangeAlternativeRoute(alternativeRouteId);

        var changed = trip.ChangeRoute(
            trip.RouteId,
            trip.EstimatedArrivalTime.AddMinutes(15));

        changed.Should().BeFalse();
        trip.AlternativeRouteId.Should().Be(alternativeRouteId);
    }

    [Fact]
    public void Create_GeneratesImmutableTripCodeFromOriginalDepartureDate()
    {
        var departure = new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero);
        var trip = CreateTrip(departure);
        var originalCode = trip.TripCode;

        trip.Reschedule(departure.AddDays(1), departure.AddDays(1).AddHours(4));

        originalCode.Should().MatchRegex("^TRIP-20260823-[0-9ABCDEFGHJKMNPQRSTVWXYZ]{8}$");
        trip.TripCode.Should().Be(originalCode);
    }

    [Fact]
    public void BackfillTripCode_DoesNotChangeExistingCode()
    {
        var trip = CreateTrip();
        var originalCode = trip.TripCode;

        trip.BackfillTripCode();
        trip.BackfillTripCode();

        trip.TripCode.Should().Be(originalCode);
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip(DateTimeOffset? departureOverride = null)
    {
        var departure = departureOverride ?? DateTimeOffset.UtcNow.AddHours(1);
        return VietRide.Trip.Domain.Entities.Trip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            departure,
            departure.AddHours(4),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            5m);
    }
}
