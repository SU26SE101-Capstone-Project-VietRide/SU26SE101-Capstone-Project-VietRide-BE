using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripSeatRepository : ITripSeatRepository
{
    private readonly TripDbContext _dbContext;

    public TripSeatRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.TripSeats.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<TripSeat> AddAsync(TripSeat entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.TripSeats.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(TripSeat entity) => _dbContext.TripSeats.Update(entity);

    public void Remove(TripSeat entity) => _dbContext.TripSeats.Remove(entity);

    public IQueryable<TripSeat> Query() => _dbContext.TripSeats;

    public IQueryable<TripSeat> QueryNoTracking() => _dbContext.TripSeats.AsNoTracking();
}
