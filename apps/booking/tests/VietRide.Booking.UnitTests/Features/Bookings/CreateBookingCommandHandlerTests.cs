using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.CreateBooking;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

/// <summary>
/// Unit tests for <see cref="CreateBookingCommandHandler"/>.
/// All external dependencies are mocked via NSubstitute.
/// </summary>
public class CreateBookingCommandHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid PassengerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TripId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OperatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid StationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid SeatLockToken = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid PaymentId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static readonly TripSnapshot ValidTrip = new(
        TripId: TripId,
        OperatorId: OperatorId,
        RouteId: Guid.NewGuid(),
        VehicleId: Guid.NewGuid(),
        Status: "SCHEDULED",
        DepartureDateTime: DateTimeOffset.UtcNow.AddHours(2),
        EstimatedArrivalTime: DateTimeOffset.UtcNow.AddHours(4),
        BaseFare: 200_000,
        OriginStation: new TripStationSnapshot(StationId, "Hà Nội"),
        DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "TP.HCM"),
        Stops: [],
        SeatSummary: new TripSeatSummary(40, 38));

    private static readonly SeatLockResult LockData = new(
        SeatLockToken: SeatLockToken,
        LockedSeats: ["A01"],
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IPaymentServiceClient _paymentClient = Substitute.For<IPaymentServiceClient>();
    private readonly IBookingService _bookingService = Substitute.For<IBookingService>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CreateBookingCommandHandler BuildSut() => new(
        _bookings, _tripClient, _paymentClient, _bookingService, _outbox, _clock,
        NullLogger<CreateBookingCommandHandler>.Instance);

    private static CreateBookingCommand BuildCommand(
        int seatCount = 1,
        string paymentMethod = "WALLET") =>
        new(
            PassengerUserId: PassengerUserId,
            TripId: TripId,
            PickupStationId: StationId,
            PickupStopId: null,
            DropoffStationId: null,
            DropoffStopId: null,
            Seats: Enumerable.Range(1, seatCount)
                .Select(i => new SeatRequest($"A{i:D2}", "Nguyen Van A", "0900000000", "012345678901"))
                .ToList(),
            VoucherCode: null,
            PaymentMethod: paymentMethod);

    // -----------------------------------------------------------------------
    // Happy path — WALLET → CONFIRMED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WalletPayment_HappyPath_ReturnsConfirmedBooking()
    {
        // Arrange
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(TripId, default).ReturnsForAnyArgs(ValidTrip);
        _tripClient.LockSeatsAsync(default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockSeatsOutcome.Success(LockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.ChargeAsync(default!, default, default, default, default!, default!, default)
            .ReturnsForAnyArgs(new ChargeOutcome.Success(new ChargeResult(PaymentId, "SUCCEEDED", null)));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);

        var handler = BuildSut();
        var command = BuildCommand(seatCount: 1, paymentMethod: "WALLET");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be("CONFIRMED");
        result.TotalAmount.Should().Be(200_000);
        result.DiscountAmount.Should().Be(0);
        result.PaymentRedirectUrl.Should().BeNull();

        // Confirm outbox was enqueued exactly once
        await _outbox.Received(1)
            .EnqueueAsync(
                Arg.Is("booking.booking.confirmed"),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());

        // Confirm BookSeats was called (seats booked after payment)
        await _tripClient.Received(1).BookSeatsAsync(
            TripId,
            SeatLockToken,
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Max seats exceeded (S5) — two guards:
    //   a) FluentValidation layer rejects > 5 seats with a validation failure.
    //   b) Handler guard throws CodedValidationException(BOOKING_MAX_SEATS_EXCEEDED)
    //      so any path that bypasses the pipeline (e.g. direct handler calls) surfaces
    //      HTTP 422 + BOOKING_MAX_SEATS_EXCEEDED (not the generic VALIDATION_ERROR).
    // -----------------------------------------------------------------------

    [Fact]
    public void Validator_MoreThanFiveSeats_ReturnsValidationFailure()
    {
        var validator = new CreateBookingCommandValidator();
        var command = BuildCommand(seatCount: 6);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.ErrorCode == "BOOKING_MAX_SEATS_EXCEEDED"
            && (e.ErrorMessage.Contains("5 seats") || e.ErrorMessage.Contains("cannot exceed")));
    }

    [Fact]
    public async Task Handle_MoreThanFiveSeats_ThrowsCodedValidationException_WithBookingMaxSeatsExceeded()
    {
        // Handler-level guard fires regardless of whether the validation pipeline ran.
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var handler = BuildSut();
        var act = () => handler.Handle(BuildCommand(seatCount: 6), CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(e => e.ErrorCode == "BOOKING_MAX_SEATS_EXCEEDED");

        // No downstream calls should have been made
        await _tripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default);
        await _bookings.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    // -----------------------------------------------------------------------
    // Trip not found → 404 TRIP_NOT_FOUND
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_TripNotFound_ThrowsCodedNotFoundException()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(TripId, default).ReturnsForAnyArgs((TripSnapshot?)null);

        var handler = BuildSut();
        var act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(e => e.ErrorCode == "TRIP_NOT_FOUND");
    }

    // -----------------------------------------------------------------------
    // Seat unavailable → 409 BOOKING_SEAT_UNAVAILABLE; no booking created
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ConcurrentSameSeat_OneWins_OneSeatUnavailable_AndCreatesOneBooking()
    {
        // Arrange: fake Trip lock contract allows only the first same-seat attempt to lock.
        var tripClient = new OneWinsTripServiceClient(ValidTrip, LockData);
        var bookings = Substitute.For<IBookingRepository>();
        var paymentClient = Substitute.For<IPaymentServiceClient>();
        var bookingService = Substitute.For<IBookingService>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var clock = Substitute.For<IClock>();
        var addCount = 0;

        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                Interlocked.Increment(ref addCount);
                return ci.Arg<BookingEntity>();
            });
        paymentClient.ChargeAsync(default!, default, default, default, default!, default!, default)
            .ReturnsForAnyArgs(new ChargeOutcome.Success(new ChargeResult(PaymentId, "SUCCEEDED", null)));

        var handler = new CreateBookingCommandHandler(
            bookings,
            tripClient,
            paymentClient,
            bookingService,
            outbox,
            clock,
            NullLogger<CreateBookingCommandHandler>.Instance);
        var command = BuildCommand(seatCount: 1, paymentMethod: "WALLET");

        // Act: two passenger attempts race for the same trip/seat.
        var attempts = await Task.WhenAll(
            CaptureAsync(() => handler.Handle(command, CancellationToken.None)),
            CaptureAsync(() => handler.Handle(command, CancellationToken.None)));

        // Assert: exactly one booking path succeeds; the loser gets the canonical code.
        attempts.Count(x => x.Result?.Status == "CONFIRMED").Should().Be(1);
        attempts.Count(x => x.Exception is ConflictException ce
            && ce.ErrorCode == "BOOKING_SEAT_UNAVAILABLE").Should().Be(1);
        addCount.Should().Be(1);
        tripClient.BookSeatsCallCount.Should().Be(1);

        await outbox.Received(1)
            .EnqueueAsync(
                Arg.Is("booking.booking.confirmed"),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SeatUnavailable_ThrowsConflictException_AndNoBookingCreated()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(TripId, default).ReturnsForAnyArgs(ValidTrip);
        _tripClient.LockSeatsAsync(default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockSeatsOutcome.SeatUnavailable(["A01"]));

        var handler = BuildSut();
        var act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_SEAT_UNAVAILABLE");

        // No booking created when lock fails
        await _bookings.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    // -----------------------------------------------------------------------
    // Trip not bookable (lock step) → 409 BOOKING_TRIP_NOT_BOOKABLE
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_TripNotBookable_AtLock_ThrowsConflictException()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(TripId, default).ReturnsForAnyArgs(ValidTrip);
        _tripClient.LockSeatsAsync(default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockSeatsOutcome.TripNotBookable("Trip closed."));

        var handler = BuildSut();
        var act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_TRIP_NOT_BOOKABLE");
    }

    // -----------------------------------------------------------------------
    // All-or-nothing + compensation: payment transport failure after lock
    // → release-seats called via IBookingService
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_PaymentTransportError_ReleasesSeats()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(TripId, default).ReturnsForAnyArgs(ValidTrip);
        _tripClient.LockSeatsAsync(default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockSeatsOutcome.Success(LockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.ChargeAsync(default!, default, default, default, default!, default!, default)
            .ReturnsForAnyArgs(new ChargeOutcome.TransportError("Payment service down."));

        var handler = BuildSut();
        var act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Payment transport error*");

        // Compensation: release-seats must have been called
        await _bookingService.Received(1)
            .ReleaseSeatsAsync(
                TripId,
                SeatLockToken,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // book-seats lock expired after payment → compensation + 409
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_BookSeatsLockExpired_ReleasesSeatsAndThrows()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(TripId, default).ReturnsForAnyArgs(ValidTrip);
        _tripClient.LockSeatsAsync(default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockSeatsOutcome.Success(LockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.ChargeAsync(default!, default, default, default, default!, default!, default)
            .ReturnsForAnyArgs(new ChargeOutcome.Success(new ChargeResult(PaymentId, "SUCCEEDED", null)));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(false); // lock expired

        var handler = BuildSut();
        var act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_SEAT_UNAVAILABLE");

        await _bookingService.Received(1)
            .ReleaseSeatsAsync(
                TripId,
                SeatLockToken,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // VNPay path — returns PENDING_PAYMENT with redirect URL
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_VNPayPayment_ReturnsPendingPaymentWithRedirectUrl()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(TripId, default).ReturnsForAnyArgs(ValidTrip);
        _tripClient.LockSeatsAsync(default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockSeatsOutcome.Success(LockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.ChargeAsync(default!, default, default, default, default!, default!, default)
            .ReturnsForAnyArgs(new ChargeOutcome.Success(
                new ChargeResult(PaymentId, "PENDING", "https://vnpay.vn/pay?token=abc")));

        var handler = BuildSut();
        var command = BuildCommand(seatCount: 1, paymentMethod: "VNPAY");
        var result = await handler.Handle(command, CancellationToken.None);

        result.Status.Should().Be("PENDING_PAYMENT");
        result.PaymentRedirectUrl.Should().Be("https://vnpay.vn/pay?token=abc");

        // No outbox event for VNPAY — not yet confirmed
        await _outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
    }

    private static async Task<AttemptResult> CaptureAsync(Func<Task<CreateBookingResult>> action)
    {
        try
        {
            return new AttemptResult(await action(), null);
        }
        catch (Exception ex)
        {
            return new AttemptResult(null, ex);
        }
    }

    private sealed record AttemptResult(CreateBookingResult? Result, Exception? Exception);

    private sealed class OneWinsTripServiceClient : ITripServiceClient
    {
        private readonly TripSnapshot _trip;
        private readonly SeatLockResult _lockData;
        private int _lockWinnerChosen;
        private int _bookSeatsCallCount;

        public OneWinsTripServiceClient(TripSnapshot trip, SeatLockResult lockData)
        {
            _trip = trip;
            _lockData = lockData;
        }

        public int BookSeatsCallCount => _bookSeatsCallCount;

        public Task<TripSnapshot?> GetTripSnapshotAsync(
            Guid tripId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TripSnapshot?>(_trip);

        public Task<LockSeatsOutcome> LockSeatsAsync(
            Guid tripId,
            IReadOnlyList<string> seatNumbers,
            Guid holdOwnerId,
            string idempotencyKey,
            int? ttlSeconds = null,
            CancellationToken cancellationToken = default)
        {
            var isWinner = Interlocked.CompareExchange(ref _lockWinnerChosen, 1, 0) == 0;
            return Task.FromResult<LockSeatsOutcome>(isWinner
                ? new LockSeatsOutcome.Success(_lockData)
                : new LockSeatsOutcome.SeatUnavailable(seatNumbers));
        }

        public Task<LockRoundTripSeatsOutcome> LockRoundTripSeatsAsync(
            Guid outboundTripId,
            IReadOnlyList<string> outboundSeatNumbers,
            Guid returnTripId,
            IReadOnlyList<string> returnSeatNumbers,
            Guid holdOwnerId,
            string idempotencyKey,
            int? ttlSeconds = null,
            CancellationToken cancellationToken = default)
        {
            var isWinner = Interlocked.CompareExchange(ref _lockWinnerChosen, 1, 0) == 0;
            return Task.FromResult<LockRoundTripSeatsOutcome>(isWinner
                ? new LockRoundTripSeatsOutcome.Success(
                    new RoundTripSeatLockResult(outboundTripId, _lockData.SeatLockToken, outboundSeatNumbers, _lockData.ExpiresAt),
                    new RoundTripSeatLockResult(returnTripId, _lockData.SeatLockToken, returnSeatNumbers, _lockData.ExpiresAt))
                : new LockRoundTripSeatsOutcome.SeatUnavailable(outboundSeatNumbers.Concat(returnSeatNumbers).ToArray()));
        }

        public Task<bool> BookSeatsAsync(
            Guid tripId,
            Guid seatLockToken,
            Guid bookingId,
            IReadOnlyList<PassengerSeatAssignment> passengerSeatAssignments,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _bookSeatsCallCount);
            return Task.FromResult(true);
        }

        public Task ReleaseSeatsAsync(
            Guid tripId,
            Guid seatLockToken,
            IReadOnlyList<string> seatNumbers,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
