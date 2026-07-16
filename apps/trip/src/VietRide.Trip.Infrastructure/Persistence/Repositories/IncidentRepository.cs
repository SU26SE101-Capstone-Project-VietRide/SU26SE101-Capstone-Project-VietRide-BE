using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class IncidentRepository : IIncidentRepository
{
    private readonly TripDbContext _dbContext;

    public IncidentRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Incidents.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<Incident> AddAsync(Incident entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.Incidents.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(Incident entity) => _dbContext.Incidents.Update(entity);

    public void Remove(Incident entity) => _dbContext.Incidents.Remove(entity);

    public IQueryable<Incident> Query() => _dbContext.Incidents;

    public IQueryable<Incident> QueryNoTracking() => _dbContext.Incidents.AsNoTracking();
}
