using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IRouteStopFareTemplateRepository : IRepository<RouteStopFareTemplate, Guid>
{
    Task<bool> ExistsOverlappingAsync(
        Guid routeId,
        Guid stopId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RouteStopFareTemplate>> ListByRouteAsync(Guid routeId, CancellationToken cancellationToken);
}
