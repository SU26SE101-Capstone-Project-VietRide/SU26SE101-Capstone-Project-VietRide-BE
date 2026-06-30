using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Boarding;

public sealed class TickPassengerBoardedCommandHandlerTests
{
    private static readonly Guid TripId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherTripId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid DriverUserId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid AssistantUserId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITripServiceClient _tripServiceClient = Substitute.For<ITripServiceClient>();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public async Task Handle_PendingPassenger_MarksBoardedAndSetsBoardedAt()
    {
        var booking = CreateConfirmedBooking(TripId);
        var passenger = booking.Passengers.Single();
        Arrange([booking], booking, CreateTripSnapshot(), Now);

        var result = await CreateHandler().Handle(
            CreateCommand(passenger.Id),
            CancellationToken.None);

        result.PassengerRecordId.Should().Be(passenger.Id);
        result.BoardingStatus.Should().Be("BOARDED");
        result.BoardedAt.Should().Be(Now);
        passenger.BoardingStatus.Should().Be(PassengerBoardingStatus.BOARDED);
        passenger.BoardedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_AlreadyBoardedPassenger_ThrowsConflict()
    {
        var booking = CreateConfirmedBooking(TripId);
        var passenger = booking.Passengers.Single();
        passenger.MarkBoarded(Now.AddMinutes(-1));
        Arrange([booking], booking, CreateTripSnapshot(), Now);

        var act = () => CreateHandler().Handle(
            CreateCommand(passenger.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(exception => exception.ErrorCode == "BOOKING_PASSENGER_ALREADY_BOARDED");
    }

    [Fact]
    public async Task Handle_PassengerFromDifferentTrip_ThrowsValidationError()
    {
        var booking = CreateConfirmedBooking(OtherTripId);
        var passenger = booking.Passengers.Single();
        Arrange([booking], booking, CreateTripSnapshot(), Now);

        var act = () => CreateHandler().Handle(
            CreateCommand(passenger.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "BOOKING_NOT_FOR_THIS_TRIP");
        passenger.BoardingStatus.Should().Be(PassengerBoardingStatus.PENDING);
    }

    [Fact]
    public async Task Handle_UnknownPassenger_ThrowsNotFound()
    {
        Arrange([], null, CreateTripSnapshot(), Now);

        var act = () => CreateHandler().Handle(
            CreateCommand(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "BOOKING_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_CallerNotAssignedToTrip_ThrowsForbiddenWithoutMutation()
    {
        var booking = CreateConfirmedBooking(TripId);
        var passenger = booking.Passengers.Single();
        Arrange([booking], booking, CreateTripSnapshot(), Now);

        var act = () => CreateHandler().Handle(
            CreateCommand(passenger.Id, Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        passenger.BoardingStatus.Should().Be(PassengerBoardingStatus.PENDING);
    }

    [Fact]
    public void Validator_EmptyIdentifiers_ReturnsValidationErrors()
    {
        var result = new TickPassengerBoardedCommandValidator().Validate(
            new TickPassengerBoardedCommand(Guid.Empty, Guid.Empty, Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
    }

    private TickPassengerBoardedCommandHandler CreateHandler()
        => new(_bookings, _tripServiceClient, _clock);

    private void Arrange(
        IReadOnlyList<BookingEntity> bookings,
        BookingEntity? trackedBooking,
        TripSnapshot trip,
        DateTimeOffset now)
    {
        _bookings.QueryNoTracking().Returns(bookings.AsQueryable());
        _bookings.FindByIdWithPassengersAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(trackedBooking);
        _tripServiceClient.GetTripSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(trip);
        _clock.UtcNow.Returns(now);
    }

    private static TickPassengerBoardedCommand CreateCommand(
        Guid passengerRecordId,
        Guid? callerUserId = null)
        => new(TripId, passengerRecordId, callerUserId ?? DriverUserId);

    private static BookingEntity CreateConfirmedBooking(Guid tripId)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(Now),
            passengerUserId: Guid.NewGuid(),
            tripId: tripId,
            operatorId: OperatorId,
            pickupStationId: Guid.NewGuid(),
            pickupStopId: null,
            dropoffStationId: Guid.NewGuid(),
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000));
        booking.AddPassenger("A01");
        booking.Confirm(Now.AddMinutes(-10));
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot()
        => new(
            TripId,
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "BOARDING",
            Now.AddHours(1),
            Now.AddHours(5),
            200_000,
            new TripStationSnapshot(Guid.NewGuid(), "Origin"),
            new TripStationSnapshot(Guid.NewGuid(), "Destination"),
            [],
            new TripSeatSummary(40, 39),
            DriverUserId: DriverUserId,
            AssistantUserId: AssistantUserId);
}
