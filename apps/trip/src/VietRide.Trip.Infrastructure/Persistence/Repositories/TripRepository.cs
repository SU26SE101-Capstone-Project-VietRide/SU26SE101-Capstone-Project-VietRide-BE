using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripRepository : ITripRepository
{
    private readonly TripDbContext dbContext;

    public TripRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => dbContext.Trips.FirstOrDefaultAsync(trip => trip.Id == id, ct);

    public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct)
    {
        dbContext.Trips.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(TripEntity entity) => dbContext.Trips.Update(entity);

    public void Remove(TripEntity entity) => dbContext.Trips.Remove(entity);

    public IQueryable<TripEntity> Query() => dbContext.Trips;

    public IQueryable<TripEntity> QueryNoTracking() => dbContext.Trips.AsNoTracking();

    public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
        => dbContext.Trips
            .Include(trip => trip.Seats)
            .FirstOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);
}
