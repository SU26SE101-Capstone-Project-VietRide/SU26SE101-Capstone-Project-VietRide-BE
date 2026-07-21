using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface IBookingPendingActionRepository : IRepository<BookingPendingAction, Guid>
{
    Task<BookingPendingAction?> GetByIdForUpdateAsync(Guid actionId, CancellationToken ct = default)
        => throw new NotSupportedException("Pending-action lock lookup is not implemented by this repository.");

    Task<BookingPendingAction?> GetByIdForUpdateSkipLockedAsync(
        Guid actionId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Non-blocking pending-action lock lookup is not implemented by this repository.");

    Task<IReadOnlyList<BookingPendingAction>> GetActiveByTripForUpdateAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Trip pending-action lock lookup is not implemented by this repository.");

    Task<BookingPendingAction?> GetActiveByBookingIdAsync(Guid bookingId, CancellationToken ct = default);

    Task<BookingPendingAction?> GetActiveByBookingIdForUpdateAsync(Guid bookingId, CancellationToken ct = default)
        => throw new NotSupportedException("Pending-action booking lock lookup is not implemented by this repository.");

    Task<IReadOnlyList<BookingPendingAction>> GetByBookingAndSourceEventAsync(
        Guid bookingId,
        Guid sourceEventId,
        CancellationToken ct = default);

    Task<IReadOnlyList<BookingPendingAction>> GetExpiredStopDisabledCandidatesAsync(
        DateTimeOffset now, CancellationToken ct = default);
}
