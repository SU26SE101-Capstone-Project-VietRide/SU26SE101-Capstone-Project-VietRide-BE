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

    public Task<TripStopFare?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.TripStopFares.FindAsync([id], cancellationToken).AsTask();
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
            .ThenBy(fare => fare.Id)
            .ToListAsync(cancellationToken);
    }
}
