using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface IBookingTransferRepository : IRepository<BookingTransfer, Guid>
{
    Task<IReadOnlyList<BookingTransfer>> GetByPassengerTripPairAsync(
        IReadOnlyCollection<Guid> passengerIds,
        Guid originalTripId,
        Guid newTripId,
        CancellationToken ct = default);
}
