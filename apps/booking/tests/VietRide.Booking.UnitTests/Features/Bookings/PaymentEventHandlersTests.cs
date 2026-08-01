using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;
using VietRide.Booking.Application.Features.Bookings.ExpireBookingOnPayment;
using VietRide.Booking.Application.Features.Bookings.MarkBookingRefunded;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class PaymentEventHandlersTests
{
    private static readonly Guid BookingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PaymentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PassengerUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TripId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SeatLockToken = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PassengerId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid BookingGroupId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid SecondTripId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid SecondSeatLockToken = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-24T10:00:00Z");

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IBookingStatusHistoryRepository _statusHistory = Substitute.For<IBookingStatusHistoryRepository>();
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IBookingService _bookingService = Substitute.For<IBookingService>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public PaymentEventHandlersTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    [Fact]
    public async Task ConfirmBookingOnPayment_WhenPending_BooksPersistedLockConfirmsAndEmitsOnce()
    {
        var booking = CreateBookingProjection(Guid.NewGuid(), 200_000);
        var snapshot = CreateSnapshot(bookingId: booking.Id);
        _bookings.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        _bookings.GetPendingPaymentTransitionSnapshotAsync(booking.Id, Arg.Any<CancellationToken>())
            .Returns(snapshot);
        _tripClient.ConfirmBookedSeatsAsync(
                TripId,
                SeatLockToken,
                booking.Id,
                Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                Arg.Any<CancellationToken>())
            .Returns(new SeatConfirmationOutcome.Success());
        _bookings.TryConfirmPendingPaymentAsync(booking.Id, Now, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new ConfirmBookingOnPaymentCommandHandler(
            _bookings,
            _tripClient,
            _outbox,
            _clock,
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(PaymentId, "BOOKING", booking.Id, 200_000),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        await _statusHistory.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(history => history.BookingId == booking.Id
                && history.Status == BookingStatus.CONFIRMED
                && history.OccurredAt == Now
                && history.Source == "CONFIRM_ON_PAYMENT"
                && history.ActorUserId == null
                && history.ReasonCode == null),
            Arg.Any<CancellationToken>());
        _ = _clock.Received(1).UtcNow;
        await _tripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default, default);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockSeatsAsync(default, default!, default, default!, default, default);
        await _outbox.Received(1)
            .EnqueueAsync(
                "booking.booking.confirmed",
                Arg.Is<string>(json => json.Contains(booking.Id.ToString(), StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmBookingOnPayment_WhenAlreadyConfirmed_IsNoOpWithoutOutbox()
    {
        var booking = CreateBookingProjection(Guid.NewGuid(), 200_000);
        booking.Confirm(Now.AddMinutes(-1));
        _bookings.QueryNoTracking().Returns(new[] { booking }.AsQueryable());

        var handler = new ConfirmBookingOnPaymentCommandHandler(
            _bookings,
            _tripClient,
            _outbox,
            _clock,
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(PaymentId, "BOOKING", booking.Id, 200_000),
            CancellationToken.None);

        transitioned.Should().BeFalse();
        await _statusHistory.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .ConfirmBookedSeatsAsync(default, default, default, default!, default);
        await _outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task ConfirmBookingOnPayment_WhenOnTimeVnPayFindsCancelled_PreservesStateAndRequestsExactRefund()
    {
        var booking = CreateBookingProjection(Guid.NewGuid(), 200_000);
        booking.Cancel(BookingCancellationReason.USER_INITIATED, Now.AddMinutes(-1));
        _bookings.QueryNoTracking().Returns(new[] { booking }.AsQueryable());

        var handler = new ConfirmBookingOnPaymentCommandHandler(
            _bookings,
            _tripClient,
            _outbox,
            _clock,
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(
                PaymentId,
                "BOOKING",
                booking.Id,
                200_000,
                "VNPAY",
                Now.AddMinutes(-1),
                Now.AddMinutes(1)),
            CancellationToken.None);

        transitioned.Should().BeFalse();
        booking.Status.Should().Be(BookingStatus.CANCELLED);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .ConfirmBookedSeatsAsync(default, default, default, default!, default);
        await _outbox.Received(1).EnqueueAsync(
            "booking.payment_refund.requested",
            Arg.Is<string>(payload =>
                payload.Contains(PaymentId.ToString(), StringComparison.OrdinalIgnoreCase)
                && payload.Contains(booking.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                && payload.Contains("SEAT_CONFIRMATION_FAILED", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmBookingOnPayment_WhenOnTimeGroupHasConfirmedLeg_ExpiresPendingLegAndRefundsBoth()
    {
        var firstBooking = CreateBookingProjection(BookingGroupId, 200_000);
        var secondBooking = CreateBookingProjection(BookingGroupId, 300_000);
        firstBooking.Confirm(Now.AddMinutes(-1));
        _bookings.QueryNoTracking().Returns(new[]
        {
            firstBooking,
            secondBooking,
        }.AsQueryable());
        _bookings.TryExpirePendingPaymentAsync(
                secondBooking.Id,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var payloads = new List<string>();
        _outbox.When(outbox => outbox.EnqueueAsync(
                "booking.payment_refund.requested",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(call => payloads.Add(call.ArgAt<string>(1)));

        var handler = new ConfirmBookingOnPaymentCommandHandler(
            _bookings,
            _tripClient,
            _outbox,
            _clock,
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(
                PaymentId,
                "BOOKING_GROUP",
                BookingGroupId,
                500_000,
                "VNPAY",
                Now.AddMinutes(-1),
                Now.AddMinutes(1)),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        firstBooking.Status.Should().Be(BookingStatus.CONFIRMED);
        await _bookings.Received(1).TryExpirePendingPaymentAsync(
            secondBooking.Id,
            Now,
            Arg.Any<CancellationToken>());
        await _tripClient.DidNotReceiveWithAnyArgs()
            .ConfirmBookedRoundTripSeatsAsync(default!, default!, default, default);
        payloads.Should().HaveCount(2);
        payloads.Should().OnlyContain(payload =>
            payload.Contains("SEAT_CONFIRMATION_FAILED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfirmBookingOnPayment_WhenBookingGroup_BatchBooksAndConfirmsBothWithoutRelocking()
    {
        var firstBooking = CreateBookingProjection(BookingGroupId, 200_000);
        var secondBooking = CreateBookingProjection(BookingGroupId, 300_000);
        var firstSnapshot = CreateSnapshot(bookingId: firstBooking.Id);
        var secondSnapshot = CreateSnapshot(
            secondBooking.Id,
            SecondTripId,
            SecondSeatLockToken,
            300_000,
            "B01");
        _bookings.QueryNoTracking().Returns(new[]
        {
            firstBooking,
            secondBooking,
        }.AsQueryable());
        _bookings.GetPendingPaymentTransitionSnapshotAsync(firstBooking.Id, Arg.Any<CancellationToken>())
            .Returns(firstSnapshot);
        _bookings.GetPendingPaymentTransitionSnapshotAsync(secondBooking.Id, Arg.Any<CancellationToken>())
            .Returns(secondSnapshot);
        _tripClient.ConfirmBookedRoundTripSeatsAsync(
                Arg.Any<RoundTripBookSeatsLeg>(),
                Arg.Any<RoundTripBookSeatsLeg>(),
                Arg.Any<CancellationToken>(),
                PaymentId)
            .Returns(new SeatConfirmationOutcome.Success());
        _bookings.TryConfirmPendingPaymentGroupAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Order().SequenceEqual(
                        new[] { firstBooking.Id, secondBooking.Id }.Order())),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new ConfirmBookingOnPaymentCommandHandler(
            _bookings,
            _tripClient,
            _outbox,
            _clock,
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(PaymentId, "BOOKING_GROUP", BookingGroupId, 500_000),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        await _tripClient.Received(1).ConfirmBookedRoundTripSeatsAsync(
            Arg.Any<RoundTripBookSeatsLeg>(),
            Arg.Any<RoundTripBookSeatsLeg>(),
            Arg.Any<CancellationToken>(),
            PaymentId);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockSeatsAsync(default, default!, default, default!, default, default);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .BookSeatsAsync(default, default, default, default!, default);
        await _statusHistory.Received(2).AddAsync(
            Arg.Is<BookingStatusHistory>(history => history.Status == BookingStatus.CONFIRMED),
            Arg.Any<CancellationToken>());
        await _outbox.Received(2).EnqueueAsync(
            "booking.booking.confirmed",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpireBookingOnPayment_WhenPending_ExpiresAndReleasesPersistedSeatLockOnce()
    {
        _bookings.GetPendingPaymentTransitionSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(CreateSnapshot());
        _bookings.TryExpirePendingPaymentAsync(BookingId, Now, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new ExpireBookingOnPaymentCommandHandler(
            _bookings,
            _bookingService,
            _clock,
            NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance,
            _statusHistory,
            Substitute.For<IVoucherService>());

        var transitioned = await handler.Handle(
            new ExpireBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        await _statusHistory.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(history => history.BookingId == BookingId
                && history.Status == BookingStatus.EXPIRED
                && history.OccurredAt == Now
                && history.Source == "EXPIRE_ON_PAYMENT"
                && history.ActorUserId == null
                && history.ReasonCode == null),
            Arg.Any<CancellationToken>());
        _ = _clock.Received(1).UtcNow;
        await _bookings.Received(1)
            .TryExpirePendingPaymentAsync(BookingId, Now, Arg.Any<CancellationToken>());
        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockSeatsAsync(default, default!, default, default!, default, default);
        await _bookingService.Received(1)
            .ReleaseSeatsAsync(
                TripId,
                SeatLockToken,
                Arg.Is<IReadOnlyList<string>>(seats => seats.SequenceEqual(new[] { "A01" })),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpireBookingOnPayment_WhenNotPending_IsNoOpWithoutSeatRelease()
    {
        _bookings.GetPendingPaymentTransitionSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns((BookingPaymentTransitionSnapshot?)null);

        var handler = new ExpireBookingOnPaymentCommandHandler(
            _bookings,
            _bookingService,
            _clock,
            NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance,
            _statusHistory,
            Substitute.For<IVoucherService>());

        var transitioned = await handler.Handle(
            new ExpireBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId),
            CancellationToken.None);

        transitioned.Should().BeFalse();
        await _statusHistory.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _bookings.DidNotReceiveWithAnyArgs()
            .TryExpirePendingPaymentAsync(default, default, default);
        await _tripClient.DidNotReceiveWithAnyArgs()
            .LockSeatsAsync(default, default!, default, default!, default, default);
        await _bookingService.DidNotReceiveWithAnyArgs()
            .ReleaseSeatsAsync(default, default, default!, default);
    }

    [Fact]
    public async Task ExpireBookingOnPayment_WhenGuardedTransitionLosesRace_DoesNotReleaseSeats()
    {
        _bookings.GetPendingPaymentTransitionSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(CreateSnapshot());
        _bookings.TryExpirePendingPaymentAsync(BookingId, Now, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new ExpireBookingOnPaymentCommandHandler(
            _bookings,
            _bookingService,
            _clock,
            NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance,
            _statusHistory,
            Substitute.For<IVoucherService>());

        var transitioned = await handler.Handle(
            new ExpireBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId),
            CancellationToken.None);

        transitioned.Should().BeFalse();
        await _statusHistory.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _bookingService.DidNotReceiveWithAnyArgs()
            .ReleaseSeatsAsync(default, default, default!, default);
    }

    [Fact]
    public async Task MarkBookingRefunded_WhenCancelled_MarksRefundedAndEmitsOnce()
    {
        _bookings.TryMarkCancelledRefundedAsync(BookingId, Now, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new MarkBookingRefundedCommandHandler(
            _bookings,
            _outbox,
            _clock,
            NullLogger<MarkBookingRefundedCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new MarkBookingRefundedCommand(PassengerUserId, 200_000, "BOOKING_REFUND", BookingId),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        await _statusHistory.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(history => history.BookingId == BookingId
                && history.Status == BookingStatus.REFUNDED
                && history.OccurredAt == Now
                && history.Source == "MARK_REFUNDED"
                && history.ActorUserId == null
                && history.ReasonCode == null),
            Arg.Any<CancellationToken>());
        _ = _clock.Received(1).UtcNow;
        await _outbox.Received(1)
            .EnqueueAsync(
                "booking.booking.refunded",
                Arg.Is<string>(json => json.Contains(BookingId.ToString(), StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkBookingRefunded_WhenDisrupted_MarksRefundedAndEmitsOnce()
    {
        var disrupted = CreateBookingProjection(Guid.NewGuid(), 200_000);
        typeof(VietRide.Booking.Domain.Entities.Booking)
            .GetProperty(nameof(VietRide.Booking.Domain.Entities.Booking.Status))!
            .SetValue(disrupted, BookingStatus.DISRUPTED);
        _bookings.QueryNoTracking().Returns(new[] { disrupted }.AsQueryable());
        _bookings.TryMarkCancelledRefundedAsync(
                disrupted.Id,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new MarkBookingRefundedCommandHandler(
            _bookings,
            _outbox,
            _clock,
            NullLogger<MarkBookingRefundedCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new MarkBookingRefundedCommand(
                PassengerUserId,
                200_000,
                "BOOKING_REFUND",
                disrupted.Id),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        await _statusHistory.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(history =>
                history.BookingId == disrupted.Id
                && history.Status == BookingStatus.REFUNDED),
            Arg.Any<CancellationToken>());
        await _outbox.Received(1).EnqueueAsync(
            "booking.booking.refunded",
            Arg.Is<string>(json =>
                json.Contains(disrupted.Id.ToString(), StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkBookingRefunded_WhenNotCancelled_IsNoOpWithoutOutbox()
    {
        _bookings.TryMarkCancelledRefundedAsync(BookingId, Now, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new MarkBookingRefundedCommandHandler(
            _bookings,
            _outbox,
            _clock,
            NullLogger<MarkBookingRefundedCommandHandler>.Instance,
            _statusHistory);

        var transitioned = await handler.Handle(
            new MarkBookingRefundedCommand(PassengerUserId, 200_000, "BOOKING_REFUND", BookingId),
            CancellationToken.None);

        transitioned.Should().BeFalse();
        await _statusHistory.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
    }

    private static VietRide.Booking.Domain.Entities.Booking CreateBookingProjection(
        Guid bookingGroupId,
        long amount)
        => VietRide.Booking.Domain.Entities.Booking.CreatePendingPayment(
            BookingCode.Generate(Now),
            PassengerUserId,
            TripId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(amount),
            Money.Zero,
            Money.FromRaw(amount),
            bookingGroupId: bookingGroupId,
            seatLockToken: SeatLockToken);

    private static BookingPaymentTransitionSnapshot CreateSnapshot(
        Guid? bookingId = null,
        Guid? tripId = null,
        Guid? seatLockToken = null,
        long totalAmount = 200_000,
        string seatNumber = "A01")
        => new(
            bookingId ?? BookingId,
            PassengerUserId,
            tripId ?? TripId,
            seatLockToken ?? SeatLockToken,
            totalAmount,
            VoucherUsageId: null,
            [new PassengerSeatAssignment(PassengerId, seatNumber)],
            ["VT-20260630-ABCDEFGH"]);
}
