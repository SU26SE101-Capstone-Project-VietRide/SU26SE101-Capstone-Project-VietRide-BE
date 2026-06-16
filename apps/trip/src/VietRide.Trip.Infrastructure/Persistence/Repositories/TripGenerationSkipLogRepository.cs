using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripGenerationSkipLogRepository : ITripGenerationSkipLogRepository
{
    private readonly TripDbContext dbContext;

    public TripGenerationSkipLogRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<TripGenerationSkipLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.TripGenerationSkipLogs.FindAsync([id], cancellationToken).AsTask();
    }

    public async Task<TripGenerationSkipLog> AddAsync(
        TripGenerationSkipLog entity,
        CancellationToken cancellationToken = default)
    {
        await dbContext.TripGenerationSkipLogs.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(TripGenerationSkipLog entity)
    {
        dbContext.TripGenerationSkipLogs.Update(entity);
    }

    public void Remove(TripGenerationSkipLog entity)
    {
        dbContext.TripGenerationSkipLogs.Remove(entity);
    }

    public IQueryable<TripGenerationSkipLog> Query()
    {
        return dbContext.TripGenerationSkipLogs;
    }

    public IQueryable<TripGenerationSkipLog> QueryNoTracking()
    {
        return dbContext.TripGenerationSkipLogs.AsNoTracking();
    }
}
