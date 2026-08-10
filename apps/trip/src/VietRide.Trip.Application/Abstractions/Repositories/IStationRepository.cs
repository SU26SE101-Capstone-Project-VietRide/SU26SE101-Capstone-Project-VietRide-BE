using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IStationRepository : IRepository<Station, Guid>
{
    IQueryable<Station> SearchByTextNoTracking(string search, bool includeLocationSnapshots)
    {
        var normalized = search.Trim().ToLowerInvariant();
        var query = QueryNoTracking();
        return includeLocationSnapshots
            ? query.Where(station => station.Name.ToLower().Contains(normalized)
                || station.City.ToLower().Contains(normalized)
                || (station.Ward != null && station.Ward.ToLower().Contains(normalized)))
            : query.Where(station => station.Name.ToLower().Contains(normalized));
    }

    Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
        string? q,
        string? city,
        string? ward,
        Guid? locationId,
        CancellationToken cancellationToken);

    Task<Station?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

    Task<Station?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

    Task<Station?> AcquireForRouteProposalApprovalAsync(Guid id, CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-proposal approval locking is not implemented by this repository.");

    Task<bool> SlugExistsAsync(
        string slug,
        Guid excludedStationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(QueryNoTracking().Any(station =>
            station.Id != excludedStationId
            && station.Slug == slug
            && station.DeletedAt == null));

    Task<IReadOnlyList<Station>> GetForMergeAsync(
        Guid primaryStationId,
        Guid duplicateStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Station merge locking is not implemented by this repository.");

    Task<int> FlattenMergeRedirectsAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Station redirect flattening is not implemented by this repository.");

    Task<int> RelinkShuttleTripsAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Shuttle station relinking is not implemented by this repository.");
}
