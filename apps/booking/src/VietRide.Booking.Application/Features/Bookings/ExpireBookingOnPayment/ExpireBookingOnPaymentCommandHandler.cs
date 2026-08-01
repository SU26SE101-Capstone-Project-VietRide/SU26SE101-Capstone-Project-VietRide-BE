using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.ExpireBookingOnPayment;

public sealed class ExpireBookingOnPaymentCommandHandler
    : IRequestHandler<ExpireBookingOnPaymentCommand, bool>
{
    private const string BookingReferenceType = "BOOKING";
    private const string BookingGroupReferenceType = "BOOKING_GROUP";

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;
    private readonly IBookingService _bookingService;
    private readonly IVoucherService _voucherService;
    private readonly IClock _clock;
    private readonly ILogger<ExpireBookingOnPaymentCommandHandler> _logger;

    public ExpireBookingOnPaymentCommandHandler(
        IBookingRepository bookings,
        IBookingService bookingService,
        IClock clock,
        ILogger<ExpireBookingOnPaymentCommandHandler> logger,
        IBookingStatusHistoryRepository statusHistory,
        IVoucherService voucherService)
    {
        _bookings = bookings;
        _statusHistory = statusHistory;
        _bookingService = bookingService;
        _clock = clock;
        _logger = logger;
        _voucherService = voucherService;
    }

    public async Task<bool> Handle(ExpireBookingOnPaymentCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, BookingGroupReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var bookingIds = _bookings.QueryNoTracking()
                .Where(x => x.BookingGroupId == request.ReferenceId)
                .Select(x => x.Id)
                .OrderBy(bookingId => bookingId)
                .ToList();
            if (bookingIds.Count != 2) return false;

            await _bookings.AcquirePaymentTransitionLocksAsync(
                bookingIds,
                cancellationToken);

            var statuses = _bookings.QueryNoTracking()
                .Where(booking => bookingIds.Contains(booking.Id))
                .ToDictionary(booking => booking.Id, booking => booking.Status);
            if (statuses.Count != 2
                || statuses.Values.Any(status =>
                    status is not (BookingStatus.PENDING_PAYMENT or BookingStatus.EXPIRED)))
            {
                return false;
            }

            var snapshots = new List<BookingPaymentTransitionSnapshot>();
            foreach (var bookingId in bookingIds.Where(id => statuses[id] == BookingStatus.PENDING_PAYMENT))
            {
                var groupSnapshot = await _bookings.GetPendingPaymentTransitionSnapshotAsync(
                    bookingId,
                    cancellationToken);
                if (groupSnapshot is null)
                {
                    throw new InvalidOperationException(
                        "Serialized round-trip expiry could not reload a pending Booking.");
                }

                snapshots.Add(groupSnapshot);
            }

            if (snapshots.Count == 0)
            {
                return false;
            }

            var groupNow = _clock.UtcNow;
            foreach (var groupSnapshot in snapshots)
            {
                if (!await _bookings.TryExpirePendingPaymentAsync(
                        groupSnapshot.BookingId,
                        groupNow,
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        "Round-trip payment expiry lost its serialized Booking transition.");
                }

                await AddHistoryAsync(groupSnapshot.BookingId, groupNow, cancellationToken);
            }

            foreach (var groupSnapshot in snapshots)
            {
                await CompensateAsync(groupSnapshot, cancellationToken);
            }

            return true;
        }

        if (!string.Equals(request.ReferenceType, BookingReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await _bookings.AcquirePaymentTransitionLocksAsync(
            [request.ReferenceId],
            cancellationToken);

        var snapshot = await _bookings.GetPendingPaymentTransitionSnapshotAsync(
            request.ReferenceId,
            cancellationToken);
        if (snapshot is null)
        {
            _logger.LogInformation(
                "Payment expired event {PaymentId} ignored for booking {BookingId}; booking is not pending payment.",
                request.PaymentId,
                request.ReferenceId);
            return false;
        }

        var now = _clock.UtcNow;
        var transitioned = await _bookings.TryExpirePendingPaymentAsync(
            request.ReferenceId,
            now,
            cancellationToken);
        if (!transitioned)
        {
            _logger.LogInformation(
                "Payment expired event {PaymentId} ignored for booking {BookingId}; booking is not pending payment.",
                request.PaymentId,
                request.ReferenceId);
            return false;
        }

        await AddHistoryAsync(request.ReferenceId, now, cancellationToken);
        await CompensateAsync(snapshot, cancellationToken);

        return true;
    }

    private async Task AddHistoryAsync(
        Guid bookingId,
        DateTimeOffset expiredAt,
        CancellationToken cancellationToken)
        => await _statusHistory.AddAsync(
            BookingStatusHistory.Create(
                bookingId,
                BookingStatus.EXPIRED,
                expiredAt,
                BookingStatusHistorySource.ExpireOnPayment),
            cancellationToken);

    private async Task CompensateAsync(
        BookingPaymentTransitionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var seatNumbers = snapshot.PassengerSeatAssignments
            .Select(p => p.SeatNumber)
            .ToArray();
        if (snapshot.SeatLockToken.HasValue && seatNumbers.Length > 0)
        {
            await _bookingService.ReleaseSeatsAsync(
                snapshot.TripId,
                snapshot.SeatLockToken.Value,
                seatNumbers,
                cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Booking {BookingId} expired without seat release because persisted seat lock metadata is missing.",
                snapshot.BookingId);
        }

        if (snapshot.VoucherUsageId.HasValue)
        {
            await _voucherService.CompensateAsync(snapshot.BookingId, cancellationToken);
        }
    }
}
