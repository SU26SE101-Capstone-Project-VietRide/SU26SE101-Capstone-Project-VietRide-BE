using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IRouteStopRepository : IRepository<RouteStop, (Guid RouteId, Guid StopId)>
{
    Task<bool> ExistsByRouteAndOrderIndexAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken);

    Task<RouteStop?> GetByRouteAndStopAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RouteStop>> ListByRouteAsync(Guid routeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RouteStop>>(
            QueryNoTracking()
                .Where(routeStop => routeStop.RouteId == routeId)
                .OrderBy(routeStop => routeStop.OrderIndex)
                .ThenBy(routeStop => routeStop.StopId)
                .ToArray());
    }

    Task<IReadOnlyList<RouteStop>> AcquireByRouteAsync(Guid routeId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-stop locking is not supported by this repository implementation.");
}
