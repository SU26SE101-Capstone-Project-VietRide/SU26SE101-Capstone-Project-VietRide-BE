using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IRouteStopRepository : IRepository<RouteStop, (Guid RouteId, Guid StopId)>
{
    Task<bool> ExistsByRouteAndOrderIndexAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken);

    Task<RouteStop?> GetByRouteAndStopAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken);
}
