using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripSeatRepository : IRepository<TripSeat, Guid>
{
    Task<IReadOnlyList<TripSeat>> AcquireForVehicleSwapAsync(
        Guid tripId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Vehicle-swap locking is not supported by this repository implementation.");
}
