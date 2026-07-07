using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;
using VietRide.Booking.Application.Features.Bookings.ExpireBookingOnPayment;
using VietRide.Booking.Application.Features.Bookings.MarkBookingRefunded;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class PaymentEventHandlersTests
{
    private static readonly Guid BookingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PaymentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PassengerUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TripId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SeatLockToken = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PassengerId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-24T10:00:00Z");

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITripServiceClient _tripClient = Substitute.For<ITripServiceClient>();
    private readonly IBookingService _bookingService = Substitute.For<IBookingService>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public PaymentEventHandlersTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    [Fact]
    public async Task ConfirmBookingOnPayment_WhenPending_BooksSeatsConfirmsAndEmitsOnce()
    {
        _bookings.GetPendingPaymentTransitionSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(CreateSnapshot());
        _tripClient.LockSeatsAsync(
                TripId,
                Arg.Is<IReadOnlyList<string>>(seats => seats.SequenceEqual(new[] { "A01" })),
                PassengerUserId,
                $"lock-{PassengerUserId}-{TripId}-A01",
                600,
                Arg.Any<CancellationToken>())
            .Returns(new LockSeatsOutcome.Success(
                new SeatLockResult(SeatLockToken, ["A01"], Now.AddMinutes(10))));
        _tripClient.BookSeatsAsync(
                TripId,
                SeatLockToken,
                BookingId,
                Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _bookings.TryConfirmPendingPaymentAsync(BookingId, Now, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new ConfirmBookingOnPaymentCommandHandler(
            _bookings,
            _tripClient,
            _outbox,
            _clock,
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance);

        var transitioned = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId, 200_000),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        await _outbox.Received(1)
            .EnqueueAsync(
                "booking.booking.confirmed",
                Arg.Is<string>(json => json.Contains(BookingId.ToString(), StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmBookingOnPayment_WhenAlreadyConfirmed_IsNoOpWithoutOutbox()
    {
        _bookings.GetPendingPaymentTransitionSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns((BookingPaymentTransitionSnapshot?)null);

        var handler = new ConfirmBookingOnPaymentCommandHandler(
            _bookings,
            _tripClient,
            _outbox,
            _clock,
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance);

        var transitioned = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId, 200_000),
            CancellationToken.None);

        transitioned.Should().BeFalse();
        await _tripClient.DidNotReceiveWithAnyArgs()
            .BookSeatsAsync(default, default, default, default!, default);
        await _outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
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
            NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance);

        var transitioned = await handler.Handle(
            new ExpireBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId),
            CancellationToken.None);

        transitioned.Should().BeTrue();
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
            NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance);

        var transitioned = await handler.Handle(
            new ExpireBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId),
            CancellationToken.None);

        transitioned.Should().BeFalse();
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
            NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance);

        var transitioned = await handler.Handle(
            new ExpireBookingOnPaymentCommand(PaymentId, "BOOKING", BookingId),
            CancellationToken.None);

        transitioned.Should().BeFalse();
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
            NullLogger<MarkBookingRefundedCommandHandler>.Instance);

        var transitioned = await handler.Handle(
            new MarkBookingRefundedCommand(PassengerUserId, 200_000, "BOOKING_REFUND", BookingId),
            CancellationToken.None);

        transitioned.Should().BeTrue();
        await _outbox.Received(1)
            .EnqueueAsync(
                "booking.booking.refunded",
                Arg.Is<string>(json => json.Contains(BookingId.ToString(), StringComparison.OrdinalIgnoreCase)),
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
            NullLogger<MarkBookingRefundedCommandHandler>.Instance);

        var transitioned = await handler.Handle(
            new MarkBookingRefundedCommand(PassengerUserId, 200_000, "BOOKING_REFUND", BookingId),
            CancellationToken.None);

        transitioned.Should().BeFalse();
        await _outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
    }

    private static BookingPaymentTransitionSnapshot CreateSnapshot()
        => new(
            BookingId,
            PassengerUserId,
            TripId,
            SeatLockToken,
            200_000,
            VoucherUsageId: null,
            [new PassengerSeatAssignment(PassengerId, "A01")],
            ["VT-20260630-ABCDEFGH"]);
}
