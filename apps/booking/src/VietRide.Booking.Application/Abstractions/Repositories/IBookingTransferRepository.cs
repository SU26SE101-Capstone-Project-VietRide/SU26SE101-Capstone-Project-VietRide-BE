using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface IBookingTransferRepository : IRepository<BookingTransfer, Guid>
{
    Task<BookingTransfer?> GetActiveForConfirmationAsync(
        Guid passengerId,
        Guid newTripId,
        Guid operatorId,
        CancellationToken ct = default);

    Task<IReadOnlyList<BookingTransfer>> GetByPassengerTripPairAsync(
        IReadOnlyCollection<Guid> passengerIds,
        Guid originalTripId,
        Guid newTripId,
        CancellationToken ct = default);

    Task<IReadOnlyList<BookingTransfer>> AcquirePendingEscalationBatchAsync(
        DateTimeOffset cutoff,
        int maxGroups,
        CancellationToken ct = default);
}
