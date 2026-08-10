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

    public async Task<TripStop?> GetForUpdateAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_trip.trip_stops WHERE trip_id = {tripId} AND stop_id = {stopId} FOR UPDATE",
            cancellationToken);
        return await _dbContext.TripStops.FirstOrDefaultAsync(
            stop => stop.TripId == tripId && stop.StopId == stopId,
            cancellationToken);
    }

    public Task<bool> TryMarkDepartedAsync(
        Guid tripId,
        Guid stopId,
        DateTimeOffset departedAt,
        CancellationToken cancellationToken)
        => TryMarkDepartedCoreAsync(tripId, stopId, departedAt, cancellationToken);

    private async Task<bool> TryMarkDepartedCoreAsync(
        Guid tripId,
        Guid stopId,
        DateTimeOffset departedAt,
        CancellationToken cancellationToken)
        => await _dbContext.TripStops
            .Where(stop => stop.TripId == tripId
                && stop.StopId == stopId
                && stop.Status == TripStopStatus.ARRIVED
                && stop.ActualDepartureTime == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(stop => stop.ActualDepartureTime, departedAt),
                cancellationToken) == 1;

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

    public async Task DeleteNonArrivedByTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        EnsureCallerTransaction();
        await _dbContext.TripStops
            .Where(stop => stop.TripId == tripId && stop.Status != TripStopStatus.ARRIVED)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in _dbContext.ChangeTracker.Entries<TripStop>()
                     .Where(entry => entry.Entity.TripId == tripId
                         && entry.Entity.Status != TripStopStatus.ARRIVED))
        {
            entry.State = EntityState.Detached;
        }
    }

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
