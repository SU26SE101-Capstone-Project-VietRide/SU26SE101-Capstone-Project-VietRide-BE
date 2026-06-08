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
}
