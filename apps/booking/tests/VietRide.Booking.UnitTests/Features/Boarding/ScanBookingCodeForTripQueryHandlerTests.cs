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
    private const string TicketCodeA01 = "VT-20260630-ABCDEFGH";
    private const string TicketCodeB02 = "VT-20260630-HGFEDCBA";

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
        var booking = CreateBooking(TripId, confirmed: true, ["A01", "B02"]);
        Arrange(booking, booking, CreateTripSnapshot());

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        result.Items.Select(item => (item.TicketCode, item.SeatNumber, item.BoardingStatus))
            .Should().Equal(
                (TicketCodeA01, "A01", "PENDING"),
                (TicketCodeB02, "B02", "PENDING"));
        result.Items.Should().OnlyContain(item =>
            item.BookingCode == BookingCodeValue
            && item.BuyerName == "Nguyen Van Buyer"
            && item.BuyerPhone == "+84888151546");
        booking.Status.Should().Be(BookingStatus.CONFIRMED);
        booking.Passengers.Should().OnlyContain(
            passenger => passenger.BoardingStatus == PassengerBoardingStatus.PENDING);
    }

    [Fact]
    public async Task Handle_ScheduledTrip_RedactsBuyerContact()
    {
        var booking = CreateBooking(TripId, confirmed: true);
        Arrange(booking, booking, CreateTripSnapshot("SCHEDULED"));

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        result.Items.Single().BookingCode.Should().Be(BookingCodeValue);
        result.Items.Single().BuyerName.Should().BeNull();
        result.Items.Single().BuyerPhone.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RedactedBuyerSnapshot_DoesNotExposeStalePhone()
    {
        var booking = CreateBooking(
            TripId,
            confirmed: true,
            buyerName: BookingBuyerSnapshotProfile.DeletedDisplayName,
            buyerPhone: "+84888151546");
        Arrange(booking, booking, CreateTripSnapshot("IN_PROGRESS"));

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        result.Items.Single().BuyerName.Should().BeNull();
        result.Items.Single().BuyerPhone.Should().BeNull();
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
            new ScanBookingCodeForTripQuery(TripId, null, bookingCode, DriverUserId));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    private ScanBookingCodeForTripQueryHandler CreateHandler()
        => new(_bookings, _tripServiceClient);

    private static ScanBookingCodeForTripQuery CreateQuery(Guid? callerUserId = null)
        => new(TripId, null, BookingCodeValue, callerUserId ?? DriverUserId);

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

    private static BookingEntity CreateBooking(
        Guid tripId,
        bool confirmed,
        IReadOnlyList<string>? seatNumbers = null,
        string? buyerName = "Nguyen Van Buyer",
        string? buyerPhone = "+84888151546")
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
            totalAmount: Money.FromRaw(200_000),
            buyerDisplayName: buyerName,
            buyerPhone: buyerPhone);

        foreach (var seatNumber in seatNumbers ?? ["A01"])
        {
            var ticketCode = seatNumber == "A01"
                ? TicketCode.Parse(TicketCodeA01)
                : TicketCode.Parse(TicketCodeB02);

            booking.AddTicketedPassenger(
                seatNumber,
                ticketCode,
                Money.FromRaw(200_000),
                Money.Zero,
                Money.FromRaw(200_000));
        }

        if (confirmed)
        {
            booking.Confirm(Now.AddMinutes(-10));
        }

        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(string status = "BOARDING")
        => new(
            TripId,
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            status,
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
