using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface IBookingPendingActionRepository : IRepository<BookingPendingAction, Guid>
{
    Task<BookingPendingAction?> GetActiveByBookingIdAsync(Guid bookingId, CancellationToken ct = default);

    Task<IReadOnlyList<BookingPendingAction>> GetByBookingAndSourceEventAsync(
        Guid bookingId,
        Guid sourceEventId,
        CancellationToken ct = default);
}
