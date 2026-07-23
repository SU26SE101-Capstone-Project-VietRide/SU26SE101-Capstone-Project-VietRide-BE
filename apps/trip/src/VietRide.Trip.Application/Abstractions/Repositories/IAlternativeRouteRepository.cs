using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IAlternativeRouteRepository : IRepository<AlternativeRoute, Guid>
{
    Task<AlternativeRoute?> GetOwnedByIdAsync(Guid operatorId, Guid alternativeRouteId, CancellationToken cancellationToken);

    Task<AlternativeRoute?> AcquireOwnedByIdAsync(
        Guid operatorId,
        Guid alternativeRouteId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Alternative-route locking is not supported by this repository implementation.");

    Task<int> CountActiveByRouteAsync(Guid routeId, CancellationToken cancellationToken);

    Task<bool> ExistsStopAsync(Guid alternativeRouteId, Guid stopId, CancellationToken cancellationToken);

    Task<bool> ExistsStopOrderIndexAsync(Guid alternativeRouteId, int orderIndex, CancellationToken cancellationToken);

    Task<IReadOnlyList<AlternativeRouteStop>> ListStopsAsync(Guid alternativeRouteId, CancellationToken cancellationToken);

    Task ReplaceStopsAsync(Guid alternativeRouteId, IReadOnlyCollection<AlternativeRouteStop> stops, CancellationToken cancellationToken);

    Task<int> RelinkDestinationForStationMergeAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Alternative-route station relinking is not implemented by this repository.");
}
