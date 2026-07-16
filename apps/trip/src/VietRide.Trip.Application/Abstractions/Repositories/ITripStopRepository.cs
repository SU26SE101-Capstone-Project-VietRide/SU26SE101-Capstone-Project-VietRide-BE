using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripStopRepository : IRepository<TripStop, (Guid TripId, Guid StopId)>
{
    Task<TripStop?> GetForUpdateAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken cancellationToken)
        => GetByIdAsync((tripId, stopId), cancellationToken);
}
