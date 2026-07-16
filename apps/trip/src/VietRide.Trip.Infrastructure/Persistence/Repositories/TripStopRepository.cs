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

    public async Task<TripStop> AddAsync(TripStop entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.TripStops.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(TripStop entity) => _dbContext.TripStops.Update(entity);

    public void Remove(TripStop entity) => _dbContext.TripStops.Remove(entity);

    public IQueryable<TripStop> Query() => _dbContext.TripStops;

    public IQueryable<TripStop> QueryNoTracking() => _dbContext.TripStops.AsNoTracking();
}
