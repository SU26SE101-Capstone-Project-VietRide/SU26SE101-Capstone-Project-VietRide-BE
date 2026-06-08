using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class StationRepository : IStationRepository
{
    private readonly TripDbContext _dbContext;

    public StationRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Station?> GetByIdAsync(Guid id, CancellationToken ct)
        => _dbContext.Stations.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Station> AddAsync(Station entity, CancellationToken ct)
    {
        _dbContext.Stations.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(Station entity)
        => _dbContext.Stations.Update(entity);

    public void Remove(Station entity)
        => _dbContext.Stations.Remove(entity);

    public IQueryable<Station> Query()
        => _dbContext.Stations;

    public IQueryable<Station> QueryNoTracking()
        => _dbContext.Stations.AsNoTracking();
}
