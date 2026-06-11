using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class RouteStopRepository : IRouteStopRepository
{
    private readonly TripDbContext dbContext;

    public RouteStopRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<RouteStop?> GetByIdAsync((Guid RouteId, Guid StopId) id, CancellationToken ct)
        => GetByRouteAndStopAsync(id.RouteId, id.StopId, ct);

    public Task<RouteStop> AddAsync(RouteStop entity, CancellationToken ct)
    {
        dbContext.RouteStops.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(RouteStop entity)
        => dbContext.RouteStops.Update(entity);

    public void Remove(RouteStop entity)
        => dbContext.RouteStops.Remove(entity);

    public IQueryable<RouteStop> Query()
        => dbContext.RouteStops;

    public IQueryable<RouteStop> QueryNoTracking()
        => dbContext.RouteStops.AsNoTracking();

    public Task<bool> ExistsByRouteAndOrderIndexAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken)
        => dbContext.RouteStops.AnyAsync(
            routeStop => routeStop.RouteId == routeId && routeStop.OrderIndex == orderIndex,
            cancellationToken);

    public Task<RouteStop?> GetByRouteAndStopAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken)
        => dbContext.RouteStops.FirstOrDefaultAsync(
            routeStop => routeStop.RouteId == routeId && routeStop.StopId == stopId,
            cancellationToken);
}
