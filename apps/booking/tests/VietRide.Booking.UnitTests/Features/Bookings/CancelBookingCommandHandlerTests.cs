using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Bookings.CancelBooking;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class CancelBookingCommandHandlerTests
{
    private static readonly Guid PassengerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherPassengerUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TripId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid StationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid SeatLockToken = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IBookingStatusHistoryRepository _statusHistory = Substitute.For<IBookingStatusHistoryRepository>();
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IOperatorServiceClient _operatorClient = Substitute.For<IOperatorServiceClient>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IBookingPendingActionRepository _pendingActions = Substitute.For<IBookingPendingActionRepository>();

    private CancelBookingCommandHandler BuildSut() => new(
        _bookings,
        _tripClient,
        _operatorClient,
        _outbox,
        _clock,
        NullLogger<CancelBookingCommandHandler>.Instance,
        _statusHistory,
        _pendingActions);

    [Fact]
    public void Validator_OperatorCancelledReason_ReturnsValidationError()
    {
        var command = BuildCommand(Guid.NewGuid()) with { Reason = "OPERATOR_CANCELLED_TRIP" };

        var result = new CancelBookingCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CancelBookingCommand.Reason));
    }

    [Fact]
    public async Task Handle_ConfirmedBooking_UsesPersistedMoneyForRefundResultAndEvent()
    {
        const long persistedTotalAmount = 200_000;
        const long currentTripBaseFare = 900_000;
        var booking = CreateBooking(
            BookingStatus.CONFIRMED,
            SeatLockToken,
            totalAmount: persistedTotalAmount);
        booking.AddPassenger("A01");
        var currentTrip = CreateTripSnapshot("SCHEDULED", Now.AddHours(24)) with
        {
            BaseFare = currentTripBaseFare,
        };
        SetupBookingTripAndOperator(booking, currentTrip);
        var capturedPayloads = new List<string>();
        await _outbox.EnqueueAsync(
            Arg.Do<Guid>(_ => { }),
            "booking.booking.cancelled",
            Arg.Do<string>(capturedPayloads.Add),
            Arg.Any<CancellationToken>());

        var result = await BuildSut().Handle(BuildCommand(booking.Id), CancellationToken.None);

        result.BookingId.Should().Be(booking.Id);
        result.Status.Should().Be("CANCELLED");
        result.RefundMethod.Should().Be("WALLET");
        result.RefundAmount.Should().Be(180_000);
        currentTrip.BaseFare.Should().NotBe(booking.TotalAmount.Amount);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default, default);
        await _tripClient.Received(1).ReleaseSeatsAsync(
            TripId,
            SeatLockToken,
                Arg.Is<IReadOnlyList<string>>(seats => seats.SequenceEqual(new[] { "A01" })),
                Arg.Any<CancellationToken>());
        await _bookings.Received(1).TryCancelAsync(
            booking.Id,
            BookingCancellationReason.USER_INITIATED,
            Now,
            false,
            Arg.Any<CancellationToken>());
        await _outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(eventId => eventId != Guid.Empty),
            "booking.booking.cancelled",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _statusHistory.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(history => history.BookingId == booking.Id
                && history.Status == BookingStatus.CANCELLED
                && history.OccurredAt == Now
                && history.Source == "CANCEL_BOOKING"
                && history.ActorUserId == PassengerUserId
                && history.ReasonCode == "USER_INITIATED"),
            Arg.Any<CancellationToken>());
        _ = _clock.Received(1).UtcNow;

        capturedPayloads.Should().ContainSingle();
        using var doc = JsonDocument.Parse(capturedPayloads[0]);
        var root = doc.RootElement;
        root.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
        root.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        root.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
        root.GetProperty("userId").GetGuid().Should().Be(PassengerUserId);
        root.GetProperty("refundAmount").GetInt64().Should().Be(180_000);
        root.GetProperty("refundOverride").GetBoolean().Should().BeFalse();
        root.GetProperty("cancellationReason").GetString().Should().Be("USER_INITIATED");
    }

    [Fact]
    public async Task Handle_PendingPaymentBooking_CancelsWithZeroRefund()
    {
        var booking = CreateBooking(BookingStatus.PENDING_PAYMENT, SeatLockToken);
        booking.AddPassenger("A01");
        SetupBookingTripAndOperator(booking, CreateTripSnapshot("SCHEDULED", Now.AddHours(24)));

        var result = await BuildSut().Handle(BuildCommand(booking.Id), CancellationToken.None);

        result.Status.Should().Be("CANCELLED");
        result.RefundAmount.Should().Be(0);
        await _outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(eventId => eventId != Guid.Empty),
            "booking.booking.cancelled",
            Arg.Is<string>(json => HasZeroRefundAmount(json)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AcquiresPaymentTransitionLockBeforeAuthoritativeReadAndCancelCas()
    {
        var booking = CreateBooking(
            BookingStatus.CONFIRMED,
            SeatLockToken,
            totalAmount: 200_000);
        booking.AddPassenger("A01");
        SetupBookingTripAndOperator(
            booking,
            CreateTripSnapshot("SCHEDULED", Now.AddHours(24)));

        await BuildSut().Handle(BuildCommand(booking.Id), CancellationToken.None);

        Received.InOrder(() =>
        {
            _ = _bookings.AcquirePaymentTransitionLocksAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.SequenceEqual(new[] { booking.Id })),
                Arg.Any<CancellationToken>());
            _ = _bookings.FindByIdWithPassengersAsync(
                booking.Id,
                Arg.Any<CancellationToken>());
            _ = _bookings.TryCancelAsync(
                booking.Id,
                BookingCancellationReason.USER_INITIATED,
                Now,
                false,
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_NonCancellableBookingStatus_ThrowsBeforeTripLookup()
    {
        var booking = CreateBooking(BookingStatus.EXPIRED, SeatLockToken);
        _bookings.FindByIdWithPassengersAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var act = () => BuildSut().Handle(BuildCommand(booking.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_NOT_CANCELLABLE");
        await _tripClient.DidNotReceiveWithAnyArgs().GetTripSnapshotAsync(default, default);
        await _outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_NonOwner_ThrowsForbidden()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, SeatLockToken);
        _bookings.FindByIdWithPassengersAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var act = () => BuildSut().Handle(
            BuildCommand(booking.Id, passengerUserId: OtherPassengerUserId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
        await _tripClient.DidNotReceiveWithAnyArgs().GetTripSnapshotAsync(default, default);
    }

    [Fact]
    public async Task Handle_TripInProgress_ThrowsNotCancellable()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, SeatLockToken);
        SetupBookingTripAndOperator(booking, CreateTripSnapshot("IN_PROGRESS", Now.AddHours(1)));

        var act = () => BuildSut().Handle(BuildCommand(booking.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_NOT_CANCELLABLE");
        await _bookings.DidNotReceiveWithAnyArgs().TryCancelAsync(default, default, default, default, default);
        await _outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_TripBoarding_CancelsSuccessfully()
    {
        // technical_context_v7 6.2 (lines 2050/2166): cancellation is allowed while the trip is
        // SCHEDULED or BOARDING; only IN_PROGRESS/COMPLETED block it.
        var booking = CreateBooking(BookingStatus.CONFIRMED, SeatLockToken);
        booking.AddPassenger("A01");
        SetupBookingTripAndOperator(booking, CreateTripSnapshot("BOARDING", Now.AddHours(1)));

        var result = await BuildSut().Handle(BuildCommand(booking.Id), CancellationToken.None);

        result.Status.Should().Be("CANCELLED");
        await _bookings.Received(1).TryCancelAsync(
            booking.Id,
            BookingCancellationReason.USER_INITIATED,
            Now,
            false,
            Arg.Any<CancellationToken>());
        await _outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(eventId => eventId != Guid.Empty),
            "booking.booking.cancelled",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LegacyBookingWithoutSeatLockToken_SkipsReleaseAndStillCancels()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, seatLockToken: null);
        booking.AddPassenger("A01");
        SetupBookingTripAndOperator(booking, CreateTripSnapshot("SCHEDULED", Now.AddHours(24)));

        var result = await BuildSut().Handle(BuildCommand(booking.Id), CancellationToken.None);

        result.Status.Should().Be("CANCELLED");
        await _tripClient.DidNotReceiveWithAnyArgs()
            .ReleaseSeatsAsync(default, default, default!, default);
        await _outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(eventId => eventId != Guid.Empty),
            "booking.booking.cancelled",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoubleCancel_WhenAtomicTransitionAlreadyLost_EnqueuesOnlyOneCancelledEvent()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, SeatLockToken);
        booking.AddPassenger("A01");
        SetupBookingTripAndOperator(booking, CreateTripSnapshot("SCHEDULED", Now.AddHours(24)));
        _bookings.TryCancelAsync(
                booking.Id,
                BookingCancellationReason.USER_INITIATED,
                Now,
                false,
                Arg.Any<CancellationToken>())
            .Returns(true, false);
        var handler = BuildSut();

        var first = await handler.Handle(BuildCommand(booking.Id), CancellationToken.None);
        var second = () => handler.Handle(BuildCommand(booking.Id), CancellationToken.None);

        first.Status.Should().Be("CANCELLED");
        await second.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_NOT_CANCELLABLE");
        await _outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(eventId => eventId != Guid.Empty),
            "booking.booking.cancelled",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _tripClient.Received(1).ReleaseSeatsAsync(
            TripId,
            SeatLockToken,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
        await _statusHistory.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(history => history.BookingId == booking.Id
                && history.Status == BookingStatus.CANCELLED
                && history.OccurredAt == Now
                && history.Source == "CANCEL_BOOKING"
                && history.ActorUserId == PassengerUserId
                && history.ReasonCode == "USER_INITIATED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RoundTripLeg_CancelsOnlyThatBooking_AndLeavesPartnerConfirmed()
    {
        var groupId = Guid.NewGuid();
        var outbound = CreateBooking(BookingStatus.CONFIRMED, SeatLockToken, groupId, TripDirection.OUTBOUND, totalAmount: 200_000);
        var returnLeg = CreateBooking(BookingStatus.CONFIRMED, Guid.NewGuid(), groupId, TripDirection.RETURN, totalAmount: 180_000);
        outbound.AddPassenger("A01");
        returnLeg.AddPassenger("B01");
        SetupBookingTripAndOperator(outbound, CreateTripSnapshot("SCHEDULED", Now.AddHours(24)));

        var result = await BuildSut().Handle(BuildCommand(outbound.Id), CancellationToken.None);

        result.Status.Should().Be("CANCELLED");
        result.RefundAmount.Should().Be(180_000);
        returnLeg.Status.Should().Be(BookingStatus.CONFIRMED);
    }

    private void SetupBookingTripAndOperator(BookingEntity booking, TripSnapshot trip)
    {
        _clock.UtcNow.Returns(Now);
        _bookings.FindByIdWithPassengersAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _bookings.TryCancelAsync(
                booking.Id,
                BookingCancellationReason.USER_INITIATED,
                Now,
                false,
                Arg.Any<CancellationToken>())
            .Returns(true);
        _tripClient.GetTripSnapshotAsync(booking.TripId, Arg.Any<CancellationToken>()).Returns(trip);
        _operatorClient.GetOperatorAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookup(
                OperatorId,
                "VietRide Limousine",
                "APPROVED",
                true,
                "ops@example.com",
                "+84901234567",
                "0312345678",
                "0312345678",
                JsonSerializer.SerializeToElement(new[]
                {
                    new { hoursBeforeDeparture = 24, feePercent = 10 },
                })));
    }

    private static CancelBookingCommand BuildCommand(Guid bookingId, Guid? passengerUserId = null)
        => new(
            BookingId: bookingId,
            PassengerUserId: passengerUserId ?? PassengerUserId,
            IdempotencyKey: "cancel-booking-idempotency-key",
            Reason: "USER_INITIATED");

    private static BookingEntity CreateBooking(
        BookingStatus status,
        Guid? seatLockToken,
        Guid? bookingGroupId = null,
        TripDirection? tripDirection = null,
        long totalAmount = 200_000)
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
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.FromRaw(200_000 - totalAmount),
            totalAmount: Money.FromRaw(totalAmount),
            tripSnapshotOriginName: "Ha Noi",
            tripSnapshotDestName: "Da Nang",
            tripSnapshotDeparture: Now.AddHours(25),
            tripSnapshotRouteName: null,
            bookingGroupId: bookingGroupId,
            tripDirection: tripDirection,
            seatLockToken: seatLockToken);

        if (status == BookingStatus.CONFIRMED)
        {
            booking.Confirm(Now.AddMinutes(-10));
        }
        else if (status == BookingStatus.EXPIRED)
        {
            booking.ExpirePayment(Now.AddMinutes(-10));
        }

        return booking;
    }

    private static bool HasZeroRefundAmount(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("refundAmount").GetInt64() == 0;
    }

    private static TripSnapshot CreateTripSnapshot(string status, DateTimeOffset departureDateTime)
        => new(
            TripId: TripId,
            OperatorId: OperatorId,
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: status,
            DepartureDateTime: departureDateTime,
            EstimatedArrivalTime: departureDateTime.AddHours(4),
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(StationId, "Ha Noi"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "Da Nang"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 38));
}
