using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripStopRepository : IRepository<TripStop, (Guid TripId, Guid StopId)>
{
    Task<IReadOnlyList<TripStop>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip-stop locking is not supported by this repository implementation.");

    void RemoveRange(IEnumerable<TripStop> stops)
        => throw new NotSupportedException("Trip-stop range removal is not supported by this repository implementation.");

    Task DeleteNonArrivedByTripAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Non-arrived Trip-stop replacement is not supported by this repository implementation.");

    Task DeleteByTripAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip-stop replacement is not supported by this repository implementation.");

    Task<TripStop?> GetForUpdateAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken cancellationToken)
        => GetByIdAsync((tripId, stopId), cancellationToken);

    Task<bool> TryMarkDepartedAsync(
        Guid tripId,
        Guid stopId,
        DateTimeOffset departedAt,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip-stop departure is not supported by this repository implementation.");
}
