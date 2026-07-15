using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripStopRepository : IRepository<TripStop, (Guid TripId, Guid StopId)>
{
    Task<IReadOnlyList<TripStop>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip-stop locking is not supported by this repository implementation.");

    void RemoveRange(IEnumerable<TripStop> stops)
        => throw new NotSupportedException("Trip-stop range removal is not supported by this repository implementation.");

    Task DeleteByTripAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip-stop replacement is not supported by this repository implementation.");
}
