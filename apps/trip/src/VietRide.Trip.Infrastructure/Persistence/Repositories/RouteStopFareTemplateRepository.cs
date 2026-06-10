using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class RouteStopFareTemplateRepository : IRouteStopFareTemplateRepository
{
    private readonly TripDbContext dbContext;

    public RouteStopFareTemplateRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<RouteStopFareTemplate?> GetByIdAsync(Guid id, CancellationToken ct)
        => dbContext.RouteStopFareTemplates.FirstOrDefaultAsync(template => template.Id == id, ct);

    public Task<RouteStopFareTemplate> AddAsync(RouteStopFareTemplate entity, CancellationToken ct)
    {
        dbContext.RouteStopFareTemplates.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(RouteStopFareTemplate entity)
        => dbContext.RouteStopFareTemplates.Update(entity);

    public void Remove(RouteStopFareTemplate entity)
        => dbContext.RouteStopFareTemplates.Remove(entity);

    public IQueryable<RouteStopFareTemplate> Query()
        => dbContext.RouteStopFareTemplates;

    public IQueryable<RouteStopFareTemplate> QueryNoTracking()
        => dbContext.RouteStopFareTemplates.AsNoTracking();

    public Task<bool> ExistsOverlappingAsync(
        Guid routeId,
        Guid stopId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        CancellationToken cancellationToken)
        => dbContext.RouteStopFareTemplates.AnyAsync(
            template => template.RouteId == routeId
                && template.StopId == stopId
                && (!effectiveUntil.HasValue || template.EffectiveFrom < effectiveUntil.Value)
                && (!template.EffectiveUntil.HasValue || template.EffectiveUntil.Value > effectiveFrom),
            cancellationToken);

    public async Task<IReadOnlyList<RouteStopFareTemplate>> ListByRouteAsync(Guid routeId, CancellationToken cancellationToken)
        => await dbContext.RouteStopFareTemplates
            .AsNoTracking()
            .Where(template => template.RouteId == routeId)
            .OrderBy(template => template.StopId)
            .ThenBy(template => template.EffectiveFrom)
            .ThenBy(template => template.Id)
            .ToListAsync(cancellationToken);
}
