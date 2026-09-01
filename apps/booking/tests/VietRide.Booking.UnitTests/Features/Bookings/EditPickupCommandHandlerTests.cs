using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.EditPickup;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.UnitTests.TestDoubles;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class EditPickupCommandHandlerTests
{
    private static readonly Guid PassengerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherPassengerUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TripId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid StationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid EqualFareStopId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HigherFareStopId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LowerFareStopId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DisallowedPickupStopId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private EditPickupCommandHandler BuildSut(IBookingStationCanonicalizer? stationCanonicalizer = null) => new(
        _bookings,
        _tripClient,
        _clock,
        stationCanonicalizer ?? PassthroughBookingStationCanonicalizer.Instance);

    [Fact]
    public async Task Handle_EqualFarePickupChange_UpdatesPickupAndReturnsZeroAmounts()
    {
        var booking = CreateConfirmedBooking();
        var trip = CreateTripSnapshot(baseFare: 200_000);
        SetupBookingAndTrip(booking, trip);

        var command = BuildCommand(booking.Id, pickupStopId: EqualFareStopId);

        var result = await BuildSut().Handle(command, CancellationToken.None);

        result.BookingId.Should().Be(booking.Id);
        result.Pickup.StationId.Should().BeNull();
        result.Pickup.StopId.Should().Be(EqualFareStopId);
        result.FareDelta.Should().Be(0);
        result.RefundAmount.Should().Be(0);
        result.PaymentRedirectUrl.Should().BeNull();
        booking.PickupStationId.Should().BeNull();
        booking.PickupStopId.Should().Be(EqualFareStopId);
        booking.PickupPointTypeSnapshot.Should().Be("STOP");
        booking.PickupPointIdSnapshot.Should().Be(EqualFareStopId);
        booking.PickupPointNameSnapshot.Should().Be("Equal fare stop");
        booking.PickupPointPlannedAtSnapshot.Should().Be(Now.AddHours(1));
        _bookings.Received(1).Update(booking);
    }

    [Fact]
    public async Task Handle_StationRedirectPersistsCanonicalPickupAfterLockedReload()
    {
        var booking = CreateConfirmedBooking();
        SetupBookingAndTrip(booking, CreateTripSnapshot(baseFare: 200_000));
        var canonicalStationId = Guid.NewGuid();
        var canonicalizer = new MappingBookingStationCanonicalizer(
            new Dictionary<Guid, Guid> { [StationId] = canonicalStationId });

        var result = await BuildSut(canonicalizer).Handle(
            BuildCommand(booking.Id, pickupStationId: StationId),
            CancellationToken.None);

        result.Pickup.StationId.Should().Be(canonicalStationId);
        booking.PickupStationId.Should().Be(canonicalStationId);
        await _bookings.Received(1).FindByIdForUpdateAsync(
            booking.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HigherFarePickupChange_ThrowsPriceChangedConflict()
    {
        var booking = CreateConfirmedBooking();
        SetupBookingAndTrip(booking, CreateTripSnapshot(baseFare: 200_000));
        var command = BuildCommand(booking.Id, pickupStopId: HigherFareStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_EDIT_PICKUP_PRICE_CHANGED");
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_LowerFarePickupChange_ThrowsPriceChangedConflict()
    {
        var booking = CreateConfirmedBooking();
        SetupBookingAndTrip(booking, CreateTripSnapshot(baseFare: 200_000));
        var command = BuildCommand(booking.Id, pickupStopId: LowerFareStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_EDIT_PICKUP_PRICE_CHANGED");
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_DisallowedPickupStop_ThrowsValidationAndDoesNotUpdate()
    {
        var booking = CreateConfirmedBooking();
        SetupBookingAndTrip(booking, CreateTripSnapshot(baseFare: 200_000));
        var command = BuildCommand(booking.Id, pickupStopId: DisallowedPickupStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(e => e.ErrorCode == "STOP_NOT_PICKUP_ALLOWED");
        booking.PickupStationId.Should().Be(StationId);
        booking.PickupStopId.Should().BeNull();
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_AfterCutoff_ThrowsCutoffExceeded()
    {
        var booking = CreateConfirmedBooking();
        var trip = CreateTripSnapshot(baseFare: 200_000, departureDateTime: Now.AddHours(2));
        SetupBookingAndTrip(booking, trip);
        var command = BuildCommand(booking.Id, pickupStopId: EqualFareStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_CUTOFF_EXCEEDED");
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_NonOwner_ThrowsForbidden()
    {
        var booking = CreateConfirmedBooking();
        _bookings.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var command = BuildCommand(booking.Id, passengerUserId: OtherPassengerUserId, pickupStopId: EqualFareStopId);
        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
        await _tripClient.DidNotReceiveWithAnyArgs().GetTripSnapshotAsync(default, default);
    }

    [Fact]
    public async Task Handle_UnknownBooking_ThrowsBookingNotFound()
    {
        var bookingId = Guid.NewGuid();
        _bookings.FindByIdAsync(bookingId, Arg.Any<CancellationToken>()).Returns((BookingEntity?)null);

        var act = () => BuildSut().Handle(BuildCommand(bookingId, pickupStationId: StationId), CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(e => e.ErrorCode == "BOOKING_NOT_FOUND");
    }

    private void SetupBookingAndTrip(BookingEntity booking, TripSnapshot trip)
    {
        _clock.UtcNow.Returns(Now);
        _bookings.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _bookings.FindByIdForUpdateAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _tripClient.GetTripSnapshotAsync(TripId, Arg.Any<CancellationToken>()).Returns(trip);
    }

    private static EditPickupCommand BuildCommand(
        Guid bookingId,
        Guid? passengerUserId = null,
        Guid? pickupStationId = null,
        Guid? pickupStopId = null)
        => new(
            BookingId: bookingId,
            PassengerUserId: passengerUserId ?? PassengerUserId,
            IdempotencyKey: "edit-pickup-idempotency-key",
            PickupStationId: pickupStationId,
            PickupStopId: pickupStopId,
            PaymentMethod: "WALLET");

    private static BookingEntity CreateConfirmedBooking(long baseFare = 200_000)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(Now),
            passengerUserId: PassengerUserId,
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: StationId,
            pickupStopId: null,
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: Money.FromRaw(baseFare),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(baseFare),
            tripSnapshotOriginName: "Hà Nội",
            tripSnapshotDestName: "Đà Nẵng",
            tripSnapshotDeparture: Now.AddHours(6),
            tripSnapshotRouteName: null);

        booking.Confirm(Now.AddMinutes(-10));
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(
        long baseFare,
        DateTimeOffset? departureDateTime = null)
        => new(
            TripId: TripId,
            OperatorId: OperatorId,
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "SCHEDULED",
            DepartureDateTime: departureDateTime ?? Now.AddHours(6),
            EstimatedArrivalTime: (departureDateTime ?? Now.AddHours(6)).AddHours(4),
            BaseFare: baseFare,
            OriginStation: new TripStationSnapshot(StationId, "Hà Nội"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "Đà Nẵng"),
            Stops:
            [
                new TripStopSnapshot(EqualFareStopId, 1, true, true, Now.AddHours(1), 42.5, baseFare, Name: "Equal fare stop"),
                new TripStopSnapshot(HigherFareStopId, 2, true, true, Now.AddHours(2), 84.0, baseFare + 50_000, Name: "Higher fare stop"),
                new TripStopSnapshot(LowerFareStopId, 3, true, true, Now.AddHours(3), 126.0, baseFare - 50_000, Name: "Lower fare stop"),
                new TripStopSnapshot(DisallowedPickupStopId, 4, false, true, Now.AddHours(4), 168.0, baseFare, Name: "Disallowed stop"),
            ],
            SeatSummary: new TripSeatSummary(40, 38));
}
