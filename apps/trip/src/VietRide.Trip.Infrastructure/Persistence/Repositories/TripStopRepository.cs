using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripStopRepository : ITripStopRepository
{
    private readonly TripDbContext _dbContext;

    public TripStopRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TripStop?> GetByIdAsync((Guid TripId, Guid StopId) id, CancellationToken cancellationToken = default) =>
        _dbContext.TripStops.FindAsync(new object[] { id.TripId, id.StopId }, cancellationToken).AsTask();

    public async Task<TripStop> AddAsync(TripStop entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.TripStops.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(TripStop entity) => _dbContext.TripStops.Update(entity);

    public void Remove(TripStop entity) => _dbContext.TripStops.Remove(entity);

    public IQueryable<TripStop> Query() => _dbContext.TripStops;

    public IQueryable<TripStop> QueryNoTracking() => _dbContext.TripStops.AsNoTracking();

    public async Task<IReadOnlyList<TripStop>> AcquireByTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        EnsureCallerTransaction();
        return await _dbContext.TripStops
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.trip_stops
                WHERE trip_id = {tripId}
                ORDER BY order_index, stop_id
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);
    }

    public void RemoveRange(IEnumerable<TripStop> stops) => _dbContext.TripStops.RemoveRange(stops);

    public async Task DeleteByTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        EnsureCallerTransaction();
        await _dbContext.TripStops
            .Where(stop => stop.TripId == tripId)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in _dbContext.ChangeTracker.Entries<TripStop>()
                     .Where(entry => entry.Entity.TripId == tripId))
        {
            entry.State = EntityState.Detached;
        }
    }

    private void EnsureCallerTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for Trip-stop acquisition.");
        }
    }
}
