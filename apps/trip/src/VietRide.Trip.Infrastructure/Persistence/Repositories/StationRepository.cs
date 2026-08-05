using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class StationRepository : IStationRepository
{
    private readonly TripDbContext _dbContext;

    public StationRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Station?> GetByIdAsync(Guid id, CancellationToken ct)
        => _dbContext.Stations.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Station?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Stations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(station => station.Id == id, cancellationToken);

    public Task<Station?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DetachLocalStations([id]);
        return _dbContext.Stations
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.stations WHERE id = {id} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Station?> AcquireForRouteProposalApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required.");
        DetachLocalStations([id]);
        return _dbContext.Stations
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.stations WHERE id = {id} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> SlugExistsAsync(
        string slug,
        Guid excludedStationId,
        CancellationToken cancellationToken = default)
        => _dbContext.Stations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(station =>
                station.Id != excludedStationId
                && station.Slug == slug
                && station.DeletedAt == null,
                cancellationToken);

    public Task<Station> AddAsync(Station entity, CancellationToken ct)
    {
        _dbContext.Stations.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(Station entity)
        => _dbContext.Stations.Update(entity);

    public void Remove(Station entity)
        => _dbContext.Stations.Remove(entity);

    public IQueryable<Station> Query()
        => _dbContext.Stations;

    public IQueryable<Station> QueryNoTracking()
        => _dbContext.Stations.AsNoTracking();

    public async Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
        string? q,
        string? city,
        string? province,
        Guid? locationId,
        CancellationToken cancellationToken)
        => await BuildSearchActiveByNameQuery(q, city, province, locationId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Station>> GetForMergeAsync(
        Guid primaryStationId,
        Guid duplicateStationId,
        CancellationToken cancellationToken = default)
    {
        var orderedIds = OrderIds(primaryStationId, duplicateStationId);
        DetachLocalStations(orderedIds);
        return await _dbContext.Stations
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.stations WHERE id IN ({orderedIds[0]}, {orderedIds[1]}) ORDER BY id::text FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    public async Task<int> FlattenMergeRedirectsAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
    {
        var redirects = await _dbContext.Stations
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.stations WHERE merged_into_station_id = {duplicateStationId} ORDER BY id::text FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        foreach (var redirect in redirects)
            redirect.FlattenMergeRedirect(primaryStationId);

        return redirects.Count;
    }

    public async Task<int> RelinkShuttleTripsAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
    {
        var shuttleTrips = await _dbContext.ShuttleTrips
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.shuttle_trips WHERE station_id = {duplicateStationId} ORDER BY id::text FOR UPDATE")
            .ToListAsync(cancellationToken);
        foreach (var shuttleTrip in shuttleTrips)
            shuttleTrip.RelinkStation(duplicateStationId, primaryStationId);

        return shuttleTrips.Count;
    }

    private IQueryable<Station> BuildSearchActiveByNameQuery(string? q, string? city, string? province, Guid? locationId)
    {
        var search = !string.IsNullOrWhiteSpace(q)
            ? _dbContext.Stations
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM vietride_trip.stations
                    WHERE deleted_at IS NULL
                      AND is_active = TRUE
                      AND unaccent(name) ILIKE unaccent('%' || {q.Trim()} || '%')
                    """)
                .AsNoTracking()
            : _dbContext.Stations
                .AsNoTracking()
                .Where(station => station.IsActive);

        if (locationId.HasValue)
        {
            var locationFilter = locationId.Value;
            search = search.Where(station => station.LocationId == locationFilter);
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            var cityFilter = city.Trim();
            search = search.Where(station => station.City == cityFilter);
        }

        if (!string.IsNullOrWhiteSpace(province))
        {
            var provinceFilter = province.Trim();
            search = search.Where(station => station.Province == provinceFilter);
        }

        return search;
    }

    private void DetachLocalStations(IReadOnlyCollection<Guid> stationIds)
    {
        var tracked = _dbContext.Stations.Local
            .Where(station => stationIds.Contains(station.Id))
            .ToArray();
        foreach (var station in tracked)
            _dbContext.Entry(station).State = EntityState.Detached;
    }

    private static Guid[] OrderIds(Guid first, Guid second)
        => new[] { first, second }
            .OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
}
