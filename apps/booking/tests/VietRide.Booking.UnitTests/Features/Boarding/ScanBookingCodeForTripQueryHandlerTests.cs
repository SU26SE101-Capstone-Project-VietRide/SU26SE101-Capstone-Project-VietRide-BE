using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Boarding;

public sealed class ScanBookingCodeForTripQueryHandlerTests
{
    private const string BookingCodeValue = "VR-20260630-ABCD2345";

    private static readonly Guid TripId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherTripId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid DriverUserId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid AssistantUserId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 3, 0, 0, TimeSpan.Zero);

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITripServiceClient _tripServiceClient = Substitute.For<ITripServiceClient>();

    [Fact]
    public async Task Handle_ConfirmedBooking_ReturnsSeatAndBoardingStatusWithoutMutation()
    {
        var booking = CreateBooking(TripId, confirmed: true);
        booking.AddPassenger("B02");
        Arrange(booking, booking, CreateTripSnapshot());

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        result.Items.Should().Equal(
            new ScanBookingCodePassengerItem("A01", "PENDING"),
            new ScanBookingCodePassengerItem("B02", "PENDING"));
        booking.Status.Should().Be(BookingStatus.CONFIRMED);
        booking.Passengers.Should().OnlyContain(
            passenger => passenger.BoardingStatus == PassengerBoardingStatus.PENDING);
    }

    [Fact]
    public async Task Handle_BookingForDifferentTrip_ThrowsBookingNotForThisTrip()
    {
        var booking = CreateBooking(OtherTripId, confirmed: true);
        Arrange(booking, booking, CreateTripSnapshot());

        var act = () => CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "BOOKING_NOT_FOR_THIS_TRIP");
        await _bookings.DidNotReceiveWithAnyArgs()
            .FindByIdWithPassengersAsync(default, default);
    }

    [Fact]
    public async Task Handle_UnknownBookingCode_ThrowsBookingNotFound()
    {
        Arrange(null, null, CreateTripSnapshot());

        var act = () => CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "BOOKING_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_NonConfirmedBooking_ThrowsBookingNotFoundWithoutMutation()
    {
        var booking = CreateBooking(TripId, confirmed: false);
        Arrange(booking, booking, CreateTripSnapshot());

        var act = () => CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "BOOKING_NOT_FOUND");
        booking.Status.Should().Be(BookingStatus.PENDING_PAYMENT);
        booking.Passengers.Single().BoardingStatus.Should().Be(PassengerBoardingStatus.PENDING);
    }

    [Fact]
    public async Task Handle_CallerNotAssignedToTrip_ThrowsForbiddenBeforeBookingLookup()
    {
        var booking = CreateBooking(TripId, confirmed: true);
        Arrange(booking, booking, CreateTripSnapshot());

        var act = () => CreateHandler().Handle(
            CreateQuery(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        await _bookings.DidNotReceiveWithAnyArgs()
            .FindByBookingCodeAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("VR-20260630-ABCD123")]
    [InlineData("vr-20260630-ABCD2345")]
    [InlineData("VR-20260630-ABCD0189")]
    public void Validator_InvalidBookingCode_ReturnsValidationError(string bookingCode)
    {
        var result = new ScanBookingCodeForTripQueryValidator().Validate(
            new ScanBookingCodeForTripQuery(TripId, bookingCode, DriverUserId));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "BookingCode");
    }

    private ScanBookingCodeForTripQueryHandler CreateHandler()
        => new(_bookings, _tripServiceClient);

    private static ScanBookingCodeForTripQuery CreateQuery(Guid? callerUserId = null)
        => new(TripId, BookingCodeValue, callerUserId ?? DriverUserId);

    private void Arrange(
        BookingEntity? bookingByCode,
        BookingEntity? bookingWithPassengers,
        TripSnapshot trip)
    {
        _tripServiceClient.GetTripSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(trip);
        _bookings.FindByBookingCodeAsync(BookingCodeValue, Arg.Any<CancellationToken>())
            .Returns(bookingByCode);
        if (bookingByCode is not null)
        {
            _bookings.FindByIdWithPassengersAsync(
                    bookingByCode.Id,
                    Arg.Any<CancellationToken>())
                .Returns(bookingWithPassengers);
        }
    }

    private static BookingEntity CreateBooking(Guid tripId, bool confirmed)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Parse(BookingCodeValue),
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
        if (confirmed)
        {
            booking.Confirm(Now.AddMinutes(-10));
        }

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
            new TripSeatSummary(40, 38),
            DriverUserId: DriverUserId,
            AssistantUserId: AssistantUserId);
}
