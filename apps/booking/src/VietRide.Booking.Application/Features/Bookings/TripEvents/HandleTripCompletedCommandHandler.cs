using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Bookings.TripEvents;

public sealed class HandleTripCompletedCommandHandler
    : IRequestHandler<HandleTripCompletedCommand, int>
{
    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;

    public HandleTripCompletedCommandHandler(
        IBookingRepository bookings,
        IBookingStatusHistoryRepository statusHistory)
    {
        _bookings = bookings;
        _statusHistory = statusHistory;
    }

    public async Task<int> Handle(
        HandleTripCompletedCommand request,
        CancellationToken cancellationToken)
    {
        var completedAt = request.CompletedAt.ToUniversalTime();
        var transitionedBookingIds = await _bookings.TryCompleteEligibleByTripIdAsync(
            request.TripId,
            completedAt,
            cancellationToken);

        foreach (var bookingId in transitionedBookingIds.Order())
        {
            await _statusHistory.AddAsync(
                BookingStatusHistory.Create(
                    bookingId,
                    BookingStatus.COMPLETED,
                    completedAt,
                    BookingStatusHistorySource.CompleteOnTripCompleted),
                cancellationToken);
        }

        return transitionedBookingIds.Count;
    }
}
