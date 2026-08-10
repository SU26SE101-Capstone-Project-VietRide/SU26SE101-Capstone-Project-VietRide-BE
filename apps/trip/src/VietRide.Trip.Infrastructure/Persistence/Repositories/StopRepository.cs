using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class StopRepository : IStopRepository
{
    private readonly TripDbContext _dbContext;

    public StopRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Stop?> GetByIdAsync(Guid id, CancellationToken ct)
        => _dbContext.Stops.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Stop>> AcquireForRouteProposalApprovalAsync(
        IReadOnlyCollection<Guid> stopIds,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required.");
        var orderedIds = stopIds.Distinct().OrderBy(id => id).ToArray();
        if (orderedIds.Length == 0) return [];
        foreach (var local in _dbContext.Stops.Local.Where(stop => orderedIds.Contains(stop.Id)).ToArray())
            _dbContext.Entry(local).State = EntityState.Detached;
        return await _dbContext.Stops
            .FromSqlRaw(
                "SELECT * FROM vietride_trip.stops WHERE id = ANY ({0}) ORDER BY id FOR UPDATE",
                orderedIds)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    public Task<Stop> AddAsync(Stop entity, CancellationToken ct)
    {
        _dbContext.Stops.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(Stop entity)
        => _dbContext.Stops.Update(entity);

    public void Remove(Stop entity)
        => _dbContext.Stops.Remove(entity);

    public IQueryable<Stop> Query()
        => _dbContext.Stops;

    public IQueryable<Stop> QueryNoTracking()
        => _dbContext.Stops.AsNoTracking();

    public IQueryable<Stop> SearchByTextNoTracking(string search)
    {
        var normalized = search.Trim();
        return _dbContext.Stops
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.stops
                WHERE deleted_at IS NULL
                  AND (unaccent(name) ILIKE unaccent('%' || {normalized} || '%')
                    OR unaccent(address) ILIKE unaccent('%' || {normalized} || '%'))
                """)
            .AsNoTracking();
    }
}
