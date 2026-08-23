using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IRouteRepository : IRepository<Route, Guid>
{
    Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken);

    Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken);

    Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken);

    Task<Route?> FindByCodeAsync(
        Guid operatorId,
        string code,
        Guid? excludedRouteId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedCode = code.Trim().ToUpperInvariant();
        return Task.FromResult(QueryNoTracking()
            .FirstOrDefault(route => route.OperatorId == operatorId
                && route.DeletedAt == null
                && route.Code == normalizedCode
                && (!excludedRouteId.HasValue || route.Id != excludedRouteId.Value)));
    }

    Task<Route?> FindDuplicateWithTransactionLockAsync(
        Guid operatorId,
        string name,
        Guid originStationId,
        Guid destinationStationId,
        Guid? excludedRouteId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedName = name.Trim().ToLowerInvariant();
        return Task.FromResult(QueryNoTracking()
            .Where(route => route.OperatorId == operatorId
                && route.DeletedAt == null
                && route.OriginStationId == originStationId
                && route.DestinationStationId == destinationStationId
                && (!excludedRouteId.HasValue || route.Id != excludedRouteId.Value))
            .AsEnumerable()
            .Where(route => route.Name.Trim().ToLowerInvariant() == normalizedName)
            .OrderBy(route => route.CreatedAt)
            .ThenBy(route => route.Id)
            .FirstOrDefault());
    }

    Task<bool> HasStationMergeConflictAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Route station-merge preflight is not implemented by this repository.");

    Task<(int OriginCount, int DestinationCount)> RelinkForStationMergeAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Route station-merge relinking is not implemented by this repository.");

    Task<Route?> AcquireOwnedActiveAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Route locking is not supported by this repository implementation.");
}
