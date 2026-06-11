using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class OperatorStationRepository : IOperatorStationRepository
{
    private readonly TripDbContext _dbContext;

    public OperatorStationRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OperatorStation?> GetByIdAsync(Guid id, CancellationToken ct)
        => _dbContext.OperatorStations.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<OperatorStation> AddAsync(OperatorStation entity, CancellationToken ct)
    {
        _dbContext.OperatorStations.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(OperatorStation entity)
        => _dbContext.OperatorStations.Update(entity);

    public void Remove(OperatorStation entity)
        => _dbContext.OperatorStations.Remove(entity);

    public IQueryable<OperatorStation> Query()
        => _dbContext.OperatorStations;

    public IQueryable<OperatorStation> QueryNoTracking()
        => _dbContext.OperatorStations.AsNoTracking();
}
