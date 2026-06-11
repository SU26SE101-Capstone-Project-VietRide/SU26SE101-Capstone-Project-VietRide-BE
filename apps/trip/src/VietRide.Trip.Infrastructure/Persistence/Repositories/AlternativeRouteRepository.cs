using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class AlternativeRouteRepository : IAlternativeRouteRepository
{
    private readonly TripDbContext dbContext;

    public AlternativeRouteRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<AlternativeRoute?> GetByIdAsync(Guid id, CancellationToken ct)
        => dbContext.AlternativeRoutes.FirstOrDefaultAsync(alternativeRoute => alternativeRoute.Id == id, ct);

    public Task<AlternativeRoute> AddAsync(AlternativeRoute entity, CancellationToken ct)
    {
        dbContext.AlternativeRoutes.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(AlternativeRoute entity)
        => dbContext.AlternativeRoutes.Update(entity);

    public void Remove(AlternativeRoute entity)
        => dbContext.AlternativeRoutes.Remove(entity);

    public IQueryable<AlternativeRoute> Query()
        => dbContext.AlternativeRoutes;

    public IQueryable<AlternativeRoute> QueryNoTracking()
        => dbContext.AlternativeRoutes.AsNoTracking();

    public Task<AlternativeRoute?> GetOwnedByIdAsync(
        Guid operatorId,
        Guid alternativeRouteId,
        CancellationToken cancellationToken)
        => dbContext.AlternativeRoutes
            .Join(
                dbContext.Routes,
                alternativeRoute => alternativeRoute.RouteId,
                route => route.Id,
                (alternativeRoute, route) => new { alternativeRoute, route })
            .Where(x => x.alternativeRoute.Id == alternativeRouteId && x.route.OperatorId == operatorId)
            .Select(x => x.alternativeRoute)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountActiveByRouteAsync(Guid routeId, CancellationToken cancellationToken)
        => dbContext.AlternativeRoutes.CountAsync(
            alternativeRoute => alternativeRoute.RouteId == routeId && alternativeRoute.IsActive,
            cancellationToken);

    public Task<bool> ExistsStopAsync(Guid alternativeRouteId, Guid stopId, CancellationToken cancellationToken)
        => dbContext.AlternativeRouteStops.AnyAsync(
            stop => stop.AlternativeRouteId == alternativeRouteId && stop.StopId == stopId,
            cancellationToken);

    public Task<bool> ExistsStopOrderIndexAsync(Guid alternativeRouteId, int orderIndex, CancellationToken cancellationToken)
        => dbContext.AlternativeRouteStops.AnyAsync(
            stop => stop.AlternativeRouteId == alternativeRouteId && stop.OrderIndex == orderIndex,
            cancellationToken);

    public async Task<IReadOnlyList<AlternativeRouteStop>> ListStopsAsync(
        Guid alternativeRouteId,
        CancellationToken cancellationToken)
        => await dbContext.AlternativeRouteStops
            .AsNoTracking()
            .Where(stop => stop.AlternativeRouteId == alternativeRouteId)
            .OrderBy(stop => stop.OrderIndex)
            .ThenBy(stop => stop.StopId)
            .ToListAsync(cancellationToken);

    public async Task ReplaceStopsAsync(
        Guid alternativeRouteId,
        IReadOnlyCollection<AlternativeRouteStop> stops,
        CancellationToken cancellationToken)
    {
        var existingStops = await dbContext.AlternativeRouteStops
            .Where(stop => stop.AlternativeRouteId == alternativeRouteId)
            .ToListAsync(cancellationToken);

        dbContext.AlternativeRouteStops.RemoveRange(existingStops);
        dbContext.AlternativeRouteStops.AddRange(stops);
    }
}
