using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripRepository : ITripRepository
{
    private readonly TripDbContext _dbContext;

    public TripRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Domain.Entities.Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Trips.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<Domain.Entities.Trip> AddAsync(Domain.Entities.Trip entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.Trips.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(Domain.Entities.Trip entity) => _dbContext.Trips.Update(entity);

    public void Remove(Domain.Entities.Trip entity) => _dbContext.Trips.Remove(entity);

    public IQueryable<Domain.Entities.Trip> Query() => _dbContext.Trips;

    public IQueryable<Domain.Entities.Trip> QueryNoTracking() => _dbContext.Trips.AsNoTracking();

    public Task<Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
        _dbContext.Trips
            .Include(trip => trip.Seats)
            .FirstOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);
}
