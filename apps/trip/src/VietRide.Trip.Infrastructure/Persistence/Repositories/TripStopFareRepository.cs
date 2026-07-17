using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripStopFareRepository : ITripStopFareRepository
{
    private readonly TripDbContext dbContext;

    public TripStopFareRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<TripStopFare?> GetByIdAsync(
        (Guid TripId, Guid StopId) id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.TripStopFares
            .FindAsync([id.TripId, id.StopId], cancellationToken)
            .AsTask();
    }

    public async Task<TripStopFare> AddAsync(
        TripStopFare entity,
        CancellationToken cancellationToken = default)
    {
        await dbContext.TripStopFares.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(TripStopFare entity)
    {
        dbContext.TripStopFares.Update(entity);
    }

    public void Remove(TripStopFare entity)
    {
        dbContext.TripStopFares.Remove(entity);
    }

    public IQueryable<TripStopFare> Query()
    {
        return dbContext.TripStopFares;
    }

    public IQueryable<TripStopFare> QueryNoTracking()
    {
        return dbContext.TripStopFares.AsNoTracking();
    }

    public async Task<IReadOnlyList<TripStopFare>> ListByTripAsync(
        Guid tripId,
        TripStopFareSource? source,
        CancellationToken cancellationToken)
    {
        var query = dbContext.TripStopFares
            .AsNoTracking()
            .Where(fare => fare.TripId == tripId);
        if (source.HasValue)
        {
            query = query.Where(fare => fare.Source == source.Value);
        }

        return await query
            .OrderBy(fare => fare.StopId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TripStopFare>> AcquireByTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for Trip-stop-fare acquisition.");
        }

        return await dbContext.TripStopFares
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.trip_stop_fares
                WHERE trip_id = {tripId}
                ORDER BY stop_id
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);
    }

    public void RemoveRange(IEnumerable<TripStopFare> fares) => dbContext.TripStopFares.RemoveRange(fares);

    public async Task DeleteByTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for Trip-stop-fare deletion.");
        }

        await dbContext.TripStopFares
            .Where(fare => fare.TripId == tripId)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in dbContext.ChangeTracker.Entries<TripStopFare>()
                     .Where(entry => entry.Entity.TripId == tripId))
        {
            entry.State = EntityState.Detached;
        }
    }
}
