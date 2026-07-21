using FluentAssertions;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Jobs;

public sealed class Day24NoShowDetectionJobTests
{
    [Fact]
    public void AlongRoute_RequiresMatchingArrivedStopAndStrictArrivalBoundary()
    {
        var anchor = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var stopId = Guid.NewGuid();
        var booking = Booking(stopId: stopId);
        booking.Confirm(anchor.AddHours(-1));
        var trip = Trip(booking.TripId, "IN_PROGRESS", null,
            [Stop(stopId, "ARRIVED", anchor)]);

        NoShowDetectionJob.TryResolveTrigger(booking, trip, anchor.AddMinutes(15), out _).Should().BeFalse();
        NoShowDetectionJob.TryResolveTrigger(booking, trip, anchor.AddMinutes(15).AddTicks(1), out var trigger)
            .Should().BeTrue();
        trigger.Should().Be("ALONG_ROUTE");
        NoShowDetectionJob.TryResolveTrigger(booking, Trip(booking.TripId, "IN_PROGRESS", null,
            [Stop(stopId, "PENDING", anchor)]), anchor.AddMinutes(16), out _).Should().BeFalse();
        NoShowDetectionJob.TryResolveTrigger(booking, Trip(booking.TripId, "IN_PROGRESS", null,
            [Stop(Guid.NewGuid(), "ARRIVED", anchor)]), anchor.AddMinutes(16), out _).Should().BeFalse();
        NoShowDetectionJob.TryResolveTrigger(booking, Trip(booking.TripId, "IN_PROGRESS", null,
            [Stop(stopId, "ARRIVED", null)]), anchor.AddMinutes(16), out _).Should().BeFalse();
    }

    [Fact]
    public void Terminal_RequiresStationShapeInProgressAndStrictDepartureBoundary()
    {
        var anchor = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var booking = Booking(stationId: Guid.NewGuid());
        booking.Confirm(anchor.AddHours(-1));

        NoShowDetectionJob.TryResolveTrigger(
            booking, Trip(booking.TripId, "IN_PROGRESS", anchor, []), anchor.AddMinutes(15), out _).Should().BeFalse();
        NoShowDetectionJob.TryResolveTrigger(
            booking, Trip(booking.TripId, "IN_PROGRESS", anchor, []), anchor.AddMinutes(16), out var trigger).Should().BeTrue();
        trigger.Should().Be("TERMINAL");
        NoShowDetectionJob.TryResolveTrigger(
            booking, Trip(booking.TripId, "SCHEDULED", anchor, []), anchor.AddMinutes(16), out _).Should().BeFalse();
        NoShowDetectionJob.TryResolveTrigger(
            booking, Trip(booking.TripId, "IN_PROGRESS", null, []), anchor.AddMinutes(16), out _).Should().BeFalse();
    }

    [Fact]
    public void DomainTransition_DerivesNoShowAndPartialNoShowWithoutTouchingBoardedPassenger()
    {
        var allPending = Booking(stationId: Guid.NewGuid());
        allPending.AddPassenger("A01");
        allPending.AddPassenger("A02");
        allPending.Confirm(DateTimeOffset.UtcNow);
        allPending.MarkPendingPassengersNoShow().Should().HaveCount(2);
        allPending.Status.Should().Be(BookingStatus.NO_SHOW);

        var mixed = Booking(stationId: Guid.NewGuid());
        var boarded = mixed.AddPassenger("B01");
        mixed.AddPassenger("B02");
        mixed.Confirm(DateTimeOffset.UtcNow);
        boarded.MarkBoarded(DateTimeOffset.UtcNow);
        mixed.MarkPendingPassengersNoShow().Should().ContainSingle();
        mixed.Status.Should().Be(BookingStatus.PARTIAL_NO_SHOW);
        boarded.BoardingStatus.Should().Be(PassengerBoardingStatus.BOARDED);
        mixed.MarkPendingPassengersNoShow().Should().BeEmpty();
    }

    private static BookingEntity Booking(Guid? stationId = null, Guid? stopId = null)
        => BookingEntity.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            stationId, stopId, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000));

    private static TripSnapshot Trip(
        Guid tripId, string status, DateTimeOffset? actualDeparture, IReadOnlyList<TripStopSnapshot> stops)
        => new(tripId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 100_000,
            new TripStationSnapshot(Guid.NewGuid(), "Origin"),
            new TripStationSnapshot(Guid.NewGuid(), "Destination"), stops, new TripSeatSummary(10, 0),
            ActualDepartureTime: actualDeparture);

    private static TripStopSnapshot Stop(Guid stopId, string status, DateTimeOffset? actualArrival)
        => new(stopId, 1, true, true, DateTimeOffset.UtcNow, 1, 100_000,
            Status: status, ActualArrivalTime: actualArrival);
}
