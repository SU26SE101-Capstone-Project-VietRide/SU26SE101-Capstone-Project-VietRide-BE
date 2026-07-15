using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripStopFareRepository : IRepository<TripStopFare, Guid>
{
    Task<IReadOnlyList<TripStopFare>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip-stop-fare locking is not supported by this repository implementation.");

    void RemoveRange(IEnumerable<TripStopFare> fares)
        => throw new NotSupportedException("Trip-stop-fare range removal is not supported by this repository implementation.");

    Task DeleteByTripAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip-stop-fare replacement is not supported by this repository implementation.");

    Task<IReadOnlyList<TripStopFare>> ListByTripAsync(
        Guid tripId,
        TripStopFareSource? source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TripStopFare>>(
            QueryNoTracking()
                .Where(fare => fare.TripId == tripId && (!source.HasValue || fare.Source == source.Value))
                .OrderBy(fare => fare.StopId)
                .ThenBy(fare => fare.Id)
                .ToList());
    }
}
