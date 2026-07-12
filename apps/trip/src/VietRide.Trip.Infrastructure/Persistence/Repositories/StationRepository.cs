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
}
