using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.EditDropoff;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.UnitTests.TestDoubles;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class EditDropoffCommandHandlerTests
{
    private static readonly Guid PassengerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherPassengerUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TripId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid OriginStationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid DestinationStationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid PickupStopId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ValidDropoffStopId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EarlierDropoffStopId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DisallowedDropoffStopId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid MissingDropoffStopId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private EditDropoffCommandHandler BuildSut(IBookingStationCanonicalizer? stationCanonicalizer = null) => new(
        _bookings,
        _tripClient,
        _clock,
        stationCanonicalizer ?? PassthroughBookingStationCanonicalizer.Instance);

    [Theory]
    [MemberData(nameof(InvalidDropoffShapeCommands))]
    public void Validator_InvalidDropoffShape_ReturnsValidationError(EditDropoffCommand command)
    {
        var result = new EditDropoffCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "dropoff");
    }

    public static TheoryData<EditDropoffCommand> InvalidDropoffShapeCommands()
        => new()
        {
            BuildCommand(Guid.NewGuid()),
            BuildCommand(Guid.NewGuid(), dropoffStationId: null, dropoffStopId: null),
            BuildCommand(Guid.NewGuid(), dropoffStationId: DestinationStationId, dropoffStopId: ValidDropoffStopId),
        };

    [Fact]
    public async Task Handle_ValidDropoffStop_UpdatesDropoffAndReturnsZeroFareDelta()
    {
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStationId: DestinationStationId);
        SetupBookingAndTrip(booking, CreateTripSnapshot());
        var command = BuildCommand(booking.Id, dropoffStopId: ValidDropoffStopId);

        var result = await BuildSut().Handle(command, CancellationToken.None);

        result.BookingId.Should().Be(booking.Id);
        result.Dropoff.StationId.Should().BeNull();
        result.Dropoff.StopId.Should().Be(ValidDropoffStopId);
        result.FareDelta.Should().Be(0);
        booking.DropoffStationId.Should().BeNull();
        booking.DropoffStopId.Should().Be(ValidDropoffStopId);
        booking.DropoffPointTypeSnapshot.Should().Be("STOP");
        booking.DropoffPointIdSnapshot.Should().Be(ValidDropoffStopId);
        booking.DropoffPointNameSnapshot.Should().Be("Valid dropoff stop");
        booking.DropoffPointPlannedAtSnapshot.Should().Be(Now.AddHours(3));
        _bookings.Received(1).Update(booking);
    }

    [Fact]
    public async Task Handle_DropoffStation_ClearsExistingDropoffStop()
    {
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStopId: ValidDropoffStopId);
        SetupBookingAndTrip(booking, CreateTripSnapshot());
        var command = BuildCommand(booking.Id, dropoffStationId: DestinationStationId);
        var canonicalStationId = Guid.NewGuid();
        var canonicalizer = new MappingBookingStationCanonicalizer(
            new Dictionary<Guid, Guid> { [DestinationStationId] = canonicalStationId });

        var result = await BuildSut(canonicalizer).Handle(command, CancellationToken.None);

        result.Dropoff.StationId.Should().Be(canonicalStationId);
        result.Dropoff.StopId.Should().BeNull();
        booking.DropoffStationId.Should().Be(canonicalStationId);
        booking.DropoffStopId.Should().BeNull();
        _bookings.Received(1).Update(booking);
    }

    [Fact]
    public async Task Handle_DropoffStationNotTripDestination_ThrowsStationNotFoundAndDoesNotUpdate()
    {
        var requestedStationId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStopId: ValidDropoffStopId);
        SetupBookingAndTrip(booking, CreateTripSnapshot());
        var command = BuildCommand(booking.Id, dropoffStationId: requestedStationId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(e => e.ErrorCode == "STATION_NOT_FOUND");
        booking.DropoffStationId.Should().BeNull();
        booking.DropoffStopId.Should().Be(ValidDropoffStopId);
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_DisallowedDropoffStop_ThrowsValidationAndDoesNotUpdate()
    {
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStationId: DestinationStationId);
        SetupBookingAndTrip(booking, CreateTripSnapshot());
        var command = BuildCommand(booking.Id, dropoffStopId: DisallowedDropoffStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(e => e.ErrorCode == "STOP_NOT_DROPOFF_ALLOWED");
        booking.DropoffStationId.Should().Be(DestinationStationId);
        booking.DropoffStopId.Should().BeNull();
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_DropoffStopBeforePickup_ThrowsValidationAndDoesNotUpdate()
    {
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStationId: DestinationStationId);
        SetupBookingAndTrip(booking, CreateTripSnapshot());
        var command = BuildCommand(booking.Id, dropoffStopId: EarlierDropoffStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(e => e.ErrorCode == "STOP_NOT_DROPOFF_ALLOWED");
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_DropoffStopNotOnRoute_ThrowsStopNotFound()
    {
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStationId: DestinationStationId);
        SetupBookingAndTrip(booking, CreateTripSnapshot());
        var command = BuildCommand(booking.Id, dropoffStopId: MissingDropoffStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(e => e.ErrorCode == "STOP_NOT_FOUND");
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_AfterCutoff_ThrowsCutoffExceeded()
    {
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStationId: DestinationStationId);
        var trip = CreateTripSnapshot(departureDateTime: Now.AddHours(2));
        SetupBookingAndTrip(booking, trip);
        var command = BuildCommand(booking.Id, dropoffStopId: ValidDropoffStopId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_CUTOFF_EXCEEDED");
        _bookings.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Handle_NonOwner_ThrowsForbidden()
    {
        var booking = CreateConfirmedBooking(pickupStopId: PickupStopId, dropoffStationId: DestinationStationId);
        _bookings.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var command = BuildCommand(booking.Id, passengerUserId: OtherPassengerUserId, dropoffStopId: ValidDropoffStopId);
        var act = () => BuildSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
        await _tripClient.DidNotReceiveWithAnyArgs().GetTripSnapshotAsync(default, default);
    }

    private void SetupBookingAndTrip(BookingEntity booking, TripSnapshot trip)
    {
        _clock.UtcNow.Returns(Now);
        _bookings.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _bookings.FindByIdForUpdateAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _tripClient.GetTripSnapshotAsync(TripId, Arg.Any<CancellationToken>()).Returns(trip);
    }

    private static EditDropoffCommand BuildCommand(
        Guid bookingId,
        Guid? passengerUserId = null,
        Guid? dropoffStationId = null,
        Guid? dropoffStopId = null)
        => new(
            BookingId: bookingId,
            PassengerUserId: passengerUserId ?? PassengerUserId,
            IdempotencyKey: "edit-dropoff-idempotency-key",
            DropoffStationId: dropoffStationId,
            DropoffStopId: dropoffStopId);

    private static BookingEntity CreateConfirmedBooking(
        Guid? pickupStopId,
        Guid? dropoffStationId = null,
        Guid? dropoffStopId = null)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(Now),
            passengerUserId: PassengerUserId,
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: pickupStopId.HasValue ? null : OriginStationId,
            pickupStopId: pickupStopId,
            dropoffStationId: dropoffStationId,
            dropoffStopId: dropoffStopId,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000),
            tripSnapshotOriginName: "Hà Nội",
            tripSnapshotDestName: "Đà Nẵng",
            tripSnapshotDeparture: Now.AddHours(6),
            tripSnapshotRouteName: null);

        booking.Confirm(Now.AddMinutes(-10));
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(DateTimeOffset? departureDateTime = null)
        => new(
            TripId: TripId,
            OperatorId: OperatorId,
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "SCHEDULED",
            DepartureDateTime: departureDateTime ?? Now.AddHours(6),
            EstimatedArrivalTime: (departureDateTime ?? Now.AddHours(6)).AddHours(4),
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(OriginStationId, "Hà Nội"),
            DestinationStation: new TripStationSnapshot(DestinationStationId, "Đà Nẵng"),
            Stops:
            [
                new TripStopSnapshot(EarlierDropoffStopId, 1, true, true, Now.AddHours(1), 42.5, 200_000, Name: "Earlier stop"),
                new TripStopSnapshot(PickupStopId, 2, true, true, Now.AddHours(2), 84.0, 200_000, Name: "Pickup stop"),
                new TripStopSnapshot(ValidDropoffStopId, 3, true, true, Now.AddHours(3), 126.0, 200_000, Name: "Valid dropoff stop"),
                new TripStopSnapshot(DisallowedDropoffStopId, 4, true, false, Now.AddHours(4), 168.0, 200_000, Name: "Disallowed dropoff stop"),
            ],
            SeatSummary: new TripSeatSummary(40, 38));
}
