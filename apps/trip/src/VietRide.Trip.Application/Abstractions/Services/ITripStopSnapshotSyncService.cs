using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Services;

public interface ITripStopSnapshotSyncService
{
    Task<TripStopSnapshotSyncPreflight> PreflightAsync(
        Guid routeId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task SynchronizeAsync(
        TripStopSnapshotSyncPreflight preflight,
        IReadOnlyList<RouteStop> targetStops,
        Guid actorUserId,
        string sourceMutation,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
