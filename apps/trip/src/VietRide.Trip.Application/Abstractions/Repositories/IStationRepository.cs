using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IStationRepository : IRepository<Station, Guid>
{
    Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
        string? q,
        string? city,
        string? province,
        Guid? locationId,
        CancellationToken cancellationToken);

    Task<Station?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

    Task<Station?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

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
