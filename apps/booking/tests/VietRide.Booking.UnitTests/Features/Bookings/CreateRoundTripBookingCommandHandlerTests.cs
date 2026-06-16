using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public class CreateRoundTripBookingCommandHandlerTests
{
    private static readonly Guid PassengerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OutboundTripId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ReturnTripId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid StationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid SeatLockToken = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static readonly TripSnapshot OutboundTrip = new(
        TripId: OutboundTripId,
        OperatorId: OperatorId,
        RouteId: Guid.NewGuid(),
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
        RouteId: Guid.NewGuid(),
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
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IPaymentServiceClient _paymentClient = Substitute.For<IPaymentServiceClient>();
    private readonly IBookingService _bookingService = Substitute.For<IBookingService>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CreateRoundTripBookingCommandHandler BuildSut() => new(
        _bookings, _tripClient, _paymentClient, _bookingService, _outbox, _clock,
        NullLogger<CreateRoundTripBookingCommandHandler>.Instance);

    private static CreateRoundTripBookingCommand BuildCommand(string paymentMethod = "WALLET") => new(
        PassengerUserId,
        "round-trip-idempotency-key",
        new CreateRoundTripBookingCommand.RoundTripBookingLegCommand(
            OutboundTripId,
            StationId,
            null,
            null,
            null,
            [new CreateRoundTripBookingCommand.RoundTripSeatRequest("A01", "Nguyen Van A", "0900000000", "012345678901")]),
        new CreateRoundTripBookingCommand.RoundTripBookingLegCommand(
            ReturnTripId,
            StationId,
            null,
            null,
            null,
            [new CreateRoundTripBookingCommand.RoundTripSeatRequest("A01", "Nguyen Van A", "0900000000", "012345678901")]),
        "SUMMER26",
        paymentMethod);

    [Fact]
    public async Task Handle_WalletPayment_HappyPath_BatchesChargeOnce_AndConfirmsBothLegs()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(ci => CreateSuccessfulBatchCharge(ci.Arg<IReadOnlyList<BatchChargeItem>>()));
        _tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);

        var result = await BuildSut().Handle(BuildCommand(), CancellationToken.None);

        result.GrandTotal.Should().Be(380_000);
        result.PaymentRedirectUrl.Should().BeNull();
        result.Outbound.TotalAmount.Should().Be(200_000);
        result.Return.TotalAmount.Should().Be(180_000);

        await _bookings.Received(1).AddAsync(
            Arg.Is<BookingEntity>(b =>
                b.BookingGroupId == result.BookingGroupId
                && b.TripDirection == TripDirection.OUTBOUND
                && b.TotalAmount.Amount == 200_000
                && b.DiscountAmount.Amount == 0),
            Arg.Any<CancellationToken>());
        await _bookings.Received(1).AddAsync(
            Arg.Is<BookingEntity>(b =>
                b.BookingGroupId == result.BookingGroupId
                && b.TripDirection == TripDirection.RETURN
                && b.TotalAmount.Amount == 180_000
                && b.DiscountAmount.Amount == 0),
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
    }

    [Fact]
    public async Task Handle_ReturnRouteMissing_ThrowsRouteReturnNotConfigured()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var outboundWithoutReturnRoute = OutboundTrip with { ReturnRouteId = null };
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<CancellationToken>()).Returns(outboundWithoutReturnRoute);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<CancellationToken>()).Returns(ReturnTrip);

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
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<CancellationToken>()).Returns(invalidReturnTrip);

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
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<CancellationToken>()).Returns(ReturnTrip);
        _tripClient.LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(OutboundLockData, ReturnLockData));
        _bookings.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BookingEntity>());
        _paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(new BatchChargeOutcome.InsufficientFunds("Insufficient wallet balance."));

        var act = () => BuildSut().Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "PAYMENT_INSUFFICIENT_WALLET");

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
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<CancellationToken>()).Returns(ReturnTrip);
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
        _tripClient.GetTripSnapshotAsync(OutboundTripId, Arg.Any<CancellationToken>()).Returns(OutboundTrip);
        _tripClient.GetTripSnapshotAsync(ReturnTripId, Arg.Any<CancellationToken>()).Returns(ReturnTrip);
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

    private static BatchChargeOutcome.Success CreateSuccessfulBatchCharge(IReadOnlyList<BatchChargeItem> items)
        => new(
            [
                new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", items[0].ReferenceId, "SUCCEEDED", null),
                new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", items[1].ReferenceId, "SUCCEEDED", null),
            ]);
}
