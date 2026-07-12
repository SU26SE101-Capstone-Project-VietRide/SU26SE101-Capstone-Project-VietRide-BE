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

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;
    private readonly IBookingService _bookingService;
    private readonly IClock _clock;
    private readonly ILogger<ExpireBookingOnPaymentCommandHandler> _logger;

    public ExpireBookingOnPaymentCommandHandler(
        IBookingRepository bookings,
        IBookingService bookingService,
        IClock clock,
        ILogger<ExpireBookingOnPaymentCommandHandler> logger,
        IBookingStatusHistoryRepository statusHistory)
    {
        _bookings = bookings;
        _statusHistory = statusHistory;
        _bookingService = bookingService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(ExpireBookingOnPaymentCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ReferenceType, BookingReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

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

        await _statusHistory.AddAsync(
            BookingStatusHistory.Create(
                request.ReferenceId,
                BookingStatus.EXPIRED,
                now,
                BookingStatusHistorySource.ExpireOnPayment),
            cancellationToken);

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

        return true;
    }
}
