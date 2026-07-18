using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.UnitTests.TestDoubles;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public class CreateRoundTripBookingCommandHandlerTests
{
    private static readonly Guid PassengerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OutboundTripId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ReturnTripId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid OutboundRouteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReturnRouteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid SeatLockToken = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static readonly TripSnapshot OutboundTrip = new(
        TripId: OutboundTripId,
        OperatorId: OperatorId,
        RouteId: OutboundRouteId,
        VehicleId: Guid.NewGuid(),
        Status: "SCHEDULED",
        DepartureDateTime: DateTimeOffset.UtcNow.AddHours(2),
        EstimatedArrivalTime: DateTimeOffset.UtcNow.AddHours(4),
        BaseFare: 200_000,
        OriginStation: new TripStationSnapshot(StationId, "Hà Nội"),
        DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "Đà Nẵng"),
        Stops: [],
        SeatSummary: new TripSeatSummary(40, 38),
        ReturnRouteId: Guid.NewGuid());

    private static readonly TripSnapshot ReturnTrip = new(
        TripId: ReturnTripId,
        OperatorId: OperatorId,
        RouteId: ReturnRouteId,
        VehicleId: Guid.NewGuid(),
        Status: "SCHEDULED",
        DepartureDateTime: DateTimeOffset.UtcNow.AddHours(6),
        EstimatedArrivalTime: DateTimeOffset.UtcNow.AddHours(8),
        BaseFare: 180_000,
        OriginStation: new TripStationSnapshot(StationId, "Đà Nẵng"),
        DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "Hà Nội"),
        Stops: [],
        SeatSummary: new TripSeatSummary(40, 39));

    private static readonly SeatLockResult LockData = new(
        SeatLockToken: SeatLockToken,
        LockedSeats: ["A01"],
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

    private static readonly RoundTripSeatLockResult OutboundLockData = new(
        TripId: OutboundTripId,
        SeatLockToken: SeatLockToken,
        LockedSeats: ["A01"],
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

    private static readonly RoundTripSeatLockResult ReturnLockData = new(
        TripId: ReturnTripId,
        SeatLockToken: Guid.Parse("99999999-9999-4999-8999-999999999999"),
        LockedSeats: ["A01"],
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IBookingStatusHistoryRepository _statusHistory = Substitute.For<IBookingStatusHistoryRepository>();
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IPaymentServiceClient _paymentClient = Substitute.For<IPaymentServiceClient>();
    private readonly IBookingService _bookingService = Substitute.For<IBookingService>();
    private readonly IVoucherService _voucherService = Substitute.For<IVoucherService>();
    private readonly IVoucherRepository _voucherRepository = Substitute.For<IVoucherRepository>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CreateRoundTripBookingCommandHandler BuildSut(IBookingStationCanonicalizer? stationCanonicalizer = null) => new(
        _bookings, _tripClient, _paymentClient, _bookingService, _voucherService, _voucherRepository, _outbox, _clock,
        NullLogger<CreateRoundTripBookingCommandHandler>.Instance, _statusHistory,
        stationCanonicalizer ?? PassthroughBookingStationCanonicalizer.Instance);

    private static CreateRoundTripBookingCommand BuildCommand(
        string paymentMethod = "WALLET",
        string? voucherCode = null,
        bool withShuttle = false) => new(
        PassengerUserId,
        "round-trip-idempotency-key",
        new CreateRoundTripBookingCommand.RoundTripBookingLegCommand(
            OutboundTripId,
            StationId,
            null,
            null,
            null,
            [new CreateRoundTripBookingCommand.RoundTripSeatRequest("A01")],
            withShuttle ? new CreateRoundTripBookingCommand.RoundTripShuttlePickupCommand("12 Nguyen Hue", 10.7731m, 106.7032m) : null),
        new CreateRoundTripBookingCommand.RoundTripBookingLegCommand(
            ReturnTripId,
            StationId,
            null,
            null,
            null,
            [new CreateRoundTripBookingCommand.RoundTripSeatRequest("A01")],
            withShuttle ? new CreateRoundTripBookingCommand.RoundTripShuttlePickupCommand("45 Le Loi", 10.7750m, 106.7010m) : null),
        voucherCode,
        paymentMethod);

    [Fact]
    public async Task Handle_WalletPayment_HappyPath_BatchesChargeOnce_AndConfirmsBothLegs()
    {
        var now = new DateTimeOffset(2026, 7, 11, 2, 3, 4, TimeSpan.Zero);
        _clock.UtcNow.Returns(now);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(ci => CreateSuccessfulBatchCharge(ci.Arg<IReadOnlyList<BatchChargeItem>>()));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);

        // No voucher code — voucherService must NOT be called
        var canonicalStationId = Guid.NewGuid();
        var canonicalizer = new MappingBookingStationCanonicalizer(
            new Dictionary<Guid, Guid> { [StationId] = canonicalStationId });
        var result = await BuildSut(canonicalizer).Handle(BuildCommand(voucherCode: null), CancellationToken.None);

        result.GrandTotal.Should().Be(380_000);
        result.PaymentRedirectUrl.Should().BeNull();
        result.Outbound.TotalAmount.Should().Be(200_000);
        result.Return.TotalAmount.Should().Be(180_000);

        await _bookings.Received(1).AddAsync(
            Arg.Is<BookingEntity>(b =>
                b.BookingGroupId == result.BookingGroupId
                && b.TripDirection == TripDirection.OUTBOUND
                && b.PickupStationId == canonicalStationId
                && b.TotalAmount.Amount == 200_000
                && b.DiscountAmount.Amount == 0
                && b.TripSnapshotDeparture == OutboundTrip.DepartureDateTime
                && b.TripCurrentDeparture == OutboundTrip.DepartureDateTime),
            Arg.Any<CancellationToken>());
        await _bookings.Received(1).AddAsync(
            Arg.Is<BookingEntity>(b =>
                b.BookingGroupId == result.BookingGroupId
                && b.TripDirection == TripDirection.RETURN
                && b.PickupStationId == canonicalStationId
                && b.TotalAmount.Amount == 180_000
                && b.DiscountAmount.Amount == 0
                && b.TripSnapshotDeparture == ReturnTrip.DepartureDateTime
                && b.TripCurrentDeparture == ReturnTrip.DepartureDateTime),
            Arg.Any<CancellationToken>());

        await _paymentClient.Received(1).BatchChargeAsync(
            PassengerUserId,
            "WALLET",
            Arg.Is<IReadOnlyList<BatchChargeItem>>(items =>
                items.Count == 2
                && items[0].ReferenceType == "BOOKING"
                && items[0].ReferenceId != Guid.Empty
                && items[0].Amount == 200_000
                && items[1].ReferenceType == "BOOKING"
                && items[1].ReferenceId != Guid.Empty
                && items[1].Amount == 180_000),
            "charge-round-trip-round-trip-idempotency-key",
            Arg.Any<CancellationToken>());

        await _tripClient.Received(1).LockRoundTripSeatsAsync(
            OutboundTripId,
            Arg.Is<IReadOnlyList<string>>(seats => seats.SequenceEqual(new[] { "A01" })),
            ReturnTripId,
            Arg.Is<IReadOnlyList<string>>(seats => seats.SequenceEqual(new[] { "A01" })),
            PassengerUserId,
            "lock-round-trip-round-trip-idempotency-key",
            600,
            Arg.Any<CancellationToken>());
        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockSeatsAsync(default, default!, default, default!, default, default);

        await _outbox.Received(2).EnqueueAsync(
            Arg.Is("booking.booking.confirmed"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await _statusHistory.Received(4).AddAsync(
            Arg.Is<BookingStatusHistory>(history =>
                (history.BookingId == result.Outbound.BookingId || history.BookingId == result.Return.BookingId)
                && (history.Status == BookingStatus.PENDING_PAYMENT || history.Status == BookingStatus.CONFIRMED)
                && history.OccurredAt == now
                && history.Source == "CREATE_ROUND_TRIP_BOOKING"
                && history.ActorUserId == PassengerUserId
                && history.ReasonCode == null),
            Arg.Any<CancellationToken>());
        _ = _clock.Received(1).UtcNow;
        await _tripClient.Received(1).GetTripSnapshotAsync(
            OutboundTripId,
            now,
            Arg.Any<CancellationToken>());
        await _tripClient.Received(1).GetTripSnapshotAsync(
            ReturnTripId,
            now,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WalletPayment_WithShuttleOnBothLegs_PersistsTwoActiveIntents()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var supportedOutbound = OutboundTrip with
        {
            OriginStation = new TripStationSnapshot(StationId, "Ha Noi", true, 21.0285m, 105.8542m, true),
        };
        var supportedReturn = ReturnTrip with
        {
            OriginStation = new TripStationSnapshot(StationId, "Da Nang", true, 16.0544m, 108.2022m, true),
        };
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(supportedOutbound);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(supportedReturn);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(call => CreateSuccessfulBatchCharge(call.Arg<IReadOnlyList<BatchChargeItem>>()));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);

        await BuildSut().Handle(BuildCommand(withShuttle: true), CancellationToken.None);

        await _bookings.Received(2).AddAsync(
            Arg.Is<BookingEntity>(booking => booking.ShuttleIntent != null && booking.ShuttleIntent.IsActive),
            Arg.Any<CancellationToken>());
        await _outbox.Received(2).EnqueueAsync(
            "booking.booking.confirmed",
            Arg.Is<string>(payload => payload.Contains("\"shuttlePickup\"", StringComparison.Ordinal)
                && payload.Contains("\"tickets\"", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnRouteMissing_ThrowsRouteReturnNotConfigured()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var outboundWithoutReturnRoute = OutboundTrip with { ReturnRouteId = null };
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(outboundWithoutReturnRoute);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);

        var act = () => BuildSut().Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(e => e.ErrorCode == "ROUTE_RETURN_NOT_CONFIGURED");

        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default);
    }

    [Fact]
    public async Task Handle_ReturnDepartureNotAfterOutboundArrival_ThrowsRoundTripInvalid()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var invalidReturnTrip = ReturnTrip with { DepartureDateTime = OutboundTrip.EstimatedArrivalTime };
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(invalidReturnTrip);

        var act = () => BuildSut().Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(e => e.ErrorCode == "BOOKING_ROUND_TRIP_INVALID");

        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default);
    }

    [Fact]
    public async Task Handle_BatchChargeFails_ReleasesBothLocksAndDoesNotConfirm()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(new BatchChargeOutcome.InsufficientFunds("Insufficient wallet balance."));

        var act = () => BuildSut().Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<VietRide.Booking.Application.Exceptions.BookingPaymentException>()
            .Where(e => e.StatusCode == 402 && e.ErrorCode == "PAYMENT_INSUFFICIENT_WALLET");

        await _bookingService.Received(1).ReleaseSeatsAsync(
            OutboundTripId,
            SeatLockToken,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
        await _bookingService.Received(1).ReleaseSeatsAsync(
            ReturnTripId,
            ReturnLockData.SeatLockToken,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
        await _tripClient.DidNotReceiveWithAnyArgs()
            .BookSeatsAsync(default, default, default, default!, default);
        await _outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_BatchChargeReturnsUnexpectedReferenceId_ReleasesBothLocksAndDoesNotConfirm()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                var items = ci.Arg<IReadOnlyList<BatchChargeItem>>();
                return new BatchChargeOutcome.Success(
                    [
                        new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", items[0].ReferenceId, "SUCCEEDED", null),
                        new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", Guid.NewGuid(), "SUCCEEDED", null),
                    ]);
            });

        var act = () => BuildSut().Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment batch charge did not return succeeded BOOKING payments for both legs.");

        await _bookingService.Received(1).ReleaseSeatsAsync(
            OutboundTripId,
            SeatLockToken,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
        await _bookingService.Received(1).ReleaseSeatsAsync(
            ReturnTripId,
            ReturnLockData.SeatLockToken,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
        await _tripClient.DidNotReceiveWithAnyArgs()
            .BookSeatsAsync(default, default, default, default!, default);
        await _outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_AtomicRoundTripLockFails_DoesNotReleaseOrCharge()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.SeatUnavailable(["A01"]));

        var act = () => BuildSut().Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "BOOKING_SEAT_UNAVAILABLE");

        await _bookingService.DidNotReceiveWithAnyArgs()
            .ReleaseSeatsAsync(default, default, default!, default);

        await _paymentClient.DidNotReceiveWithAnyArgs()
            .BatchChargeAsync(default, default!, default!, default!, default);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockSeatsAsync(default, default!, default, default!, default, default);
    }

    // -----------------------------------------------------------------------
    // Task 14.4 — round-trip voucher: 2 VoucherUsage records, per-leg apply
    // -----------------------------------------------------------------------

    /// <summary>
    /// Happy path: voucher applies to both legs independently.
    /// Asserts: 2 RecordUsageAsync calls (each with its own bookingId + shared bookingGroupId),
    /// per-leg discounts reflected in result, grandTotal = sum of discounted totals,
    /// outbox payload carries non-null voucherUsageId per leg.
    /// </summary>
    [Fact]
    public async Task Handle_WithVoucher_BothLegsMetMinOrder_Creates2VoucherUsageRows_AndReducesGrandTotal()
    {
        // Arrange
        var voucherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var outboundUsageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var returnUsageId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        const long outboundDiscount = 20_000;
        const long returnDiscount = 18_000;

        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(ci => CreateSuccessfulBatchCharge(ci.Arg<IReadOnlyList<BatchChargeItem>>()));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);

        _voucherService.ValidateAndComputeDiscountAsync(
                default!, OutboundTripId == Guid.Empty ? default : OperatorId, OutboundRouteId, default, default!, default, default)
            .ReturnsForAnyArgs(ci =>
            {
                var routeId = ci.ArgAt<Guid>(2);
                var discount = routeId == OutboundRouteId ? outboundDiscount : returnDiscount;
                return Task.FromResult(new VoucherValidationResult(voucherId, Money.FromRaw(discount)));
            });

        _voucherService.RecordUsageAsync(
                voucherId, default, default, default, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                // Return different usageIds based on call order via the bookingId arg
                // (first call = outbound, second call = return)
                return Task.FromResult(outboundUsageId);
            });
        // NSubstitute returns the same value for both calls — we capture them via outbox JSON instead.

        // Group-level cap: unlimited voucher (TotalUsageLimit = null, PerUserLimit = null)
        // — ComputeAllowedLegsAsync returns allowed = 2 so both legs keep their discounts.
        var unlimitedVoucher = BuildTestVoucher(totalUsageLimit: null, perUserLimit: null);
        _voucherRepository.GetByIdAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns(unlimitedVoucher);

        // Capture outbox JSON payloads
        var capturedJsonPayloads = new List<string>();
        await _outbox.EnqueueAsync(
            Arg.Any<string>(),
            Arg.Do<string>(json => capturedJsonPayloads.Add(json)),
            Arg.Any<CancellationToken>());

        // Act
        var result = await BuildSut().Handle(BuildCommand(voucherCode: "SUMMER26"), CancellationToken.None);

        // Assert: discounts per leg
        result.Outbound.DiscountAmount.Should().Be(outboundDiscount,
            "outbound leg discount must equal what ValidateAndComputeDiscountAsync returned for outbound route");
        result.Outbound.TotalAmount.Should().Be(200_000 - outboundDiscount,
            "outbound total must be baseFare minus discount");
        result.Return.DiscountAmount.Should().Be(returnDiscount,
            "return leg discount must equal what ValidateAndComputeDiscountAsync returned for return route");
        result.Return.TotalAmount.Should().Be(180_000 - returnDiscount,
            "return total must be baseFare minus discount");
        result.GrandTotal.Should().Be(
            (200_000 - outboundDiscount) + (180_000 - returnDiscount),
            "grandTotal must reflect both per-leg discounts");

        // Assert: RecordUsageAsync called twice — once per leg
        await _voucherService.Received(2).RecordUsageAsync(
            voucherId,
            PassengerUserId,
            Arg.Any<Guid>(),  // bookingId differs per leg
            result.BookingGroupId,
            Arg.Any<Money>(),
            Arg.Any<CancellationToken>());

        // Assert: both legs' booking_group_id match
        await _voucherService.Received(1).RecordUsageAsync(
            voucherId, PassengerUserId, Arg.Any<Guid>(), result.BookingGroupId,
            Arg.Is<Money>(m => m.Amount == outboundDiscount), Arg.Any<CancellationToken>());
        await _voucherService.Received(1).RecordUsageAsync(
            voucherId, PassengerUserId, Arg.Any<Guid>(), result.BookingGroupId,
            Arg.Is<Money>(m => m.Amount == returnDiscount), Arg.Any<CancellationToken>());

        // Assert: outbox enqueued 2 events, each carrying voucherUsageId
        capturedJsonPayloads.Should().HaveCount(2, "one confirmed event per leg");
        foreach (var json in capturedJsonPayloads)
        {
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("voucherUsageId", out var prop).Should().BeTrue(
                "each leg's outbox payload must include voucherUsageId");
            prop.ValueKind.Should().NotBe(JsonValueKind.Null,
                "voucherUsageId must not be null when a voucher was applied");
        }
    }

    /// <summary>
    /// If only one leg meets the min-order, only that leg gets a VoucherUsage row.
    /// The other leg's discount = 0 and no RecordUsageAsync call for it.
    /// </summary>
    [Fact]
    public async Task Handle_WithVoucher_OnlyOutboundMeetsMinOrder_OnlyOutboundGetsDiscount()
    {
        // Arrange: outbound succeeds, return throws VOUCHER_MIN_ORDER_NOT_MET
        var voucherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");
        var outboundUsageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000020");
        const long outboundDiscount = 20_000;

        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(ci => CreateSuccessfulBatchCharge(ci.Arg<IReadOnlyList<BatchChargeItem>>()));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);

        // Outbound: success
        _voucherService.ValidateAndComputeDiscountAsync(
                default!, default, OutboundRouteId, default, default!, default, default)
            .ReturnsForAnyArgs(ci =>
            {
                var routeId = ci.ArgAt<Guid>(2);
                if (routeId == OutboundRouteId)
                {
                    return Task.FromResult(new VoucherValidationResult(voucherId, Money.FromRaw(outboundDiscount)));
                }

                // Return leg — throw min-order not met
                throw new CodedValidationException("VOUCHER_MIN_ORDER_NOT_MET", "Min order not met.");
            });

        _voucherService.RecordUsageAsync(
                voucherId, default, default, default, default!, default)
            .ReturnsForAnyArgs(outboundUsageId);

        // Only outbound validated — legsWantingVoucher=1; cap returns 1 regardless of limits.
        var unlimitedVoucher = BuildTestVoucher(totalUsageLimit: null, perUserLimit: null);
        _voucherRepository.GetByIdAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns(unlimitedVoucher);

        // Act
        var result = await BuildSut().Handle(BuildCommand(voucherCode: "SUMMER26"), CancellationToken.None);

        // Assert: outbound discounted, return not
        result.Outbound.DiscountAmount.Should().Be(outboundDiscount);
        result.Outbound.TotalAmount.Should().Be(200_000 - outboundDiscount);
        result.Return.DiscountAmount.Should().Be(0, "return leg did not meet min-order so no discount");
        result.Return.TotalAmount.Should().Be(180_000);
        result.GrandTotal.Should().Be((200_000 - outboundDiscount) + 180_000);

        // Assert: RecordUsageAsync called only once (outbound only)
        await _voucherService.Received(1).RecordUsageAsync(
            voucherId, PassengerUserId, Arg.Any<Guid>(), result.BookingGroupId,
            Arg.Is<Money>(m => m.Amount == outboundDiscount), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // B1 — TOCTOU group-level usage-limit cap
    // -----------------------------------------------------------------------

    /// <summary>
    /// B1 regression: a voucher with totalUsageLimit=1 on a round-trip where BOTH legs meet
    /// min-order. Both per-leg ValidateAndComputeDiscountAsync calls pass (they both see
    /// currentTotal=0 &lt; 1). The group-level cap reads the snapshot count (still 0) and caps
    /// allowed=1 — only the outbound leg gets the discount; the return leg is treated as
    /// no-voucher (discount=0, no usage row). Exactly ONE RecordUsageAsync call must be made.
    /// </summary>
    [Fact]
    public async Task Handle_WithVoucher_TotalUsageLimit1_BothLegsMetMinOrder_OnlyOutboundGetsDiscount()
    {
        // Arrange
        var voucherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
        var outboundUsageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000099");
        const long discount = 15_000;

        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(ci => CreateSuccessfulBatchCharge(ci.Arg<IReadOnlyList<BatchChargeItem>>()));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);

        // Both legs pass per-leg validation (VoucherService sees stale count=0 < limit=1).
        _voucherService.ValidateAndComputeDiscountAsync(default!, default, default, default, default!, default, default)
            .ReturnsForAnyArgs(new VoucherValidationResult(voucherId, Money.FromRaw(discount)));

        _voucherService.RecordUsageAsync(voucherId, default, default, default, default!, default)
            .ReturnsForAnyArgs(outboundUsageId);

        // Group-level cap: totalUsageLimit=1, currentTotal=0 → remaining=1 → allowed=1 (outbound only).
        var limitedVoucher = BuildTestVoucher(totalUsageLimit: 1, perUserLimit: null);
        _voucherRepository.GetByIdAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns(limitedVoucher);
        // Snapshot count: 0 usages written yet.
        _voucherRepository.CountUsagesAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns(0);

        // Act
        var result = await BuildSut().Handle(BuildCommand(voucherCode: "LIMIT1"), CancellationToken.None);

        // Assert: only outbound leg discounted
        result.Outbound.DiscountAmount.Should().Be(discount,
            "outbound leg must get the discount (outbound-first priority)");
        result.Outbound.TotalAmount.Should().Be(200_000 - discount);
        result.Return.DiscountAmount.Should().Be(0,
            "return leg must NOT get the discount — group cap allows only 1 leg");
        result.Return.TotalAmount.Should().Be(180_000,
            "return total must be full baseFare with no discount");
        result.GrandTotal.Should().Be((200_000 - discount) + 180_000);

        // Assert: exactly ONE voucher usage row created (outbound only)
        await _voucherService.Received(1).RecordUsageAsync(
            voucherId,
            PassengerUserId,
            Arg.Any<Guid>(),
            result.BookingGroupId,
            Arg.Is<Money>(m => m.Amount == discount),
            Arg.Any<CancellationToken>());
        await _voucherService.DidNotReceive().RecordUsageAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Is<Money>(m => m.Amount == 0),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // S1 — non-WALLET ChargeOutcome.InsufficientFunds compensates voucher usages
    // -----------------------------------------------------------------------

    /// <summary>
    /// S1: When a non-WALLET (VNPay / other) charge returns InsufficientFunds,
    /// voucher usage rows written before the charge must be physically deleted
    /// (CompensateAsync called for the outbound usage).
    /// </summary>
    [Fact]
    public async Task Handle_NonWalletChargeInsufficientFunds_CompensatesVoucherUsages()
    {
        // Arrange
        var voucherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000030");
        var outboundUsageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000031");
        const long outboundDiscount = 10_000;

        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());

        // Only outbound gets discount; return throws min-order so no usage row there.
        _voucherService.ValidateAndComputeDiscountAsync(default!, default, default, default, default!, default, default)
            .ReturnsForAnyArgs(ci =>
            {
                var routeId = ci.ArgAt<Guid>(2);
                if (routeId == OutboundRouteId)
                    return Task.FromResult(new VoucherValidationResult(voucherId, Money.FromRaw(outboundDiscount)));
                throw new CodedValidationException("VOUCHER_MIN_ORDER_NOT_MET", "Min order not met.");
            });

        _voucherService.RecordUsageAsync(voucherId, default, default, default, default!, default)
            .ReturnsForAnyArgs(outboundUsageId);

        var unlimitedVoucher = BuildTestVoucher(totalUsageLimit: null, perUserLimit: null);
        _voucherRepository.GetByIdAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns(unlimitedVoucher);

        // Non-WALLET charge path returns InsufficientFunds.
        _paymentClient.ChargeAsync(default!, default, default, default, default!, default!, default)
            .ReturnsForAnyArgs(new ChargeOutcome.InsufficientFunds("Not enough balance."));

        var act = () => BuildSut().Handle(BuildCommand(paymentMethod: "VNPAY", voucherCode: "VCODE"), CancellationToken.None);

        // Act & Assert
        await act.Should().ThrowAsync<VietRide.Booking.Application.Exceptions.BookingPaymentException>()
            .Where(e => e.StatusCode == 402 && e.ErrorCode == "PAYMENT_INSUFFICIENT_WALLET");

        // Seats released
        await _bookingService.Received(1).ReleaseSeatsAsync(
            OutboundTripId, SeatLockToken, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _bookingService.Received(1).ReleaseSeatsAsync(
            ReturnTripId, ReturnLockData.SeatLockToken, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

        // Voucher usage compensated for the outbound booking that had a usage row.
        // The outbound booking ID is assigned dynamically by CreatePendingPayment, so we
        // capture it via the AddAsync call to verify the exact ID passed to CompensateAsync.
        var capturedOutboundBooking = (BookingEntity)_bookings.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IBookingRepository.AddAsync))
            .GetArguments()[0]!;
        await _voucherService.Received(1).CompensateAsync(
            capturedOutboundBooking.Id, Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // S2 — unexpected exception after usage rows written compensates voucher usages
    // -----------------------------------------------------------------------

    /// <summary>
    /// S2: When EnsureWalletBatchSucceeded (or any non-ConflictException) throws after voucher
    /// usage rows were written, CompensateSeatsAndVouchersAsync is called — both seats and usage
    /// rows are released/deleted. Verify via CompensateAsync being invoked.
    /// </summary>
    [Fact]
    public async Task Handle_WalletBatchSuccessValidationFails_CompensatesSeatsAndVoucherUsages()
    {
        // Arrange: voucher applies to outbound only.
        var voucherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000040");
        var outboundUsageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000041");
        const long outboundDiscount = 10_000;

        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());

        _voucherService.ValidateAndComputeDiscountAsync(default!, default, default, default, default!, default, default)
            .ReturnsForAnyArgs(ci =>
            {
                var routeId = ci.ArgAt<Guid>(2);
                if (routeId == OutboundRouteId)
                    return Task.FromResult(new VoucherValidationResult(voucherId, Money.FromRaw(outboundDiscount)));
                throw new CodedValidationException("VOUCHER_MIN_ORDER_NOT_MET", "Min order not met.");
            });

        _voucherService.RecordUsageAsync(voucherId, default, default, default, default!, default)
            .ReturnsForAnyArgs(outboundUsageId);

        var unlimitedVoucher = BuildTestVoucher(totalUsageLimit: null, perUserLimit: null);
        _voucherRepository.GetByIdAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns(unlimitedVoucher);

        // BatchCharge returns a malformed success → EnsureWalletBatchSucceeded throws InvalidOperationException.
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(new BatchChargeOutcome.Success(
                [
                    new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", Guid.NewGuid(), "SUCCEEDED", null),
                    new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", Guid.NewGuid(), "SUCCEEDED", null),
                ]));

        var act = () => BuildSut().Handle(BuildCommand(voucherCode: "VCODE"), CancellationToken.None);

        // Act & Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Seats released
        await _bookingService.Received(1).ReleaseSeatsAsync(
            OutboundTripId, SeatLockToken, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _bookingService.Received(1).ReleaseSeatsAsync(
            ReturnTripId, ReturnLockData.SeatLockToken, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

        // Voucher usage compensated for the outbound booking that had a usage row.
        await _voucherService.Received(1).CompensateAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal <see cref="Voucher"/> entity for use in unit tests.
    /// Uses Voucher.Create so the entity is in a valid state.
    /// The voucher's Id is assigned by Create (random); callers that need a specific
    /// voucherId must stub <c>_voucherRepository.GetByIdAsync</c> with
    /// <c>Arg.Any&lt;Guid&gt;()</c> so the Id mismatch does not matter.
    /// </summary>
    private static Voucher BuildTestVoucher(int? totalUsageLimit, int? perUserLimit)
    {
        var now = DateTimeOffset.UtcNow;
        return Voucher.Create(
            code: "TESTCODE",
            name: "Test Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 15_000,
            minOrderAmount: Money.FromRaw(0),
            maxDiscountAmount: null,
            totalUsageLimit: totalUsageLimit,
            perUserLimit: perUserLimit,
            validFrom: now.AddDays(-1),
            validUntil: now.AddDays(30),
            applicableOperatorIds: null,
            applicableRouteIds: null,
            fundingType: VoucherFundingType.VIETRIDE_FUNDED,
            ownerOperatorId: null,
            createdByUserId: Guid.NewGuid());
    }

    private static BatchChargeOutcome.Success CreateSuccessfulBatchCharge(IReadOnlyList<BatchChargeItem> items)
        => new(
            [
                new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", items[0].ReferenceId, "SUCCEEDED", null),
                new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", items[1].ReferenceId, "SUCCEEDED", null),
            ]);
}
