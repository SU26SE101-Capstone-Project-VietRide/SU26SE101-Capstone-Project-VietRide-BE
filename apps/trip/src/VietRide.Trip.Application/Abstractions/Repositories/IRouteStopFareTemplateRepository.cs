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

    Task<IReadOnlyList<RouteStopFareTemplate>> ListActiveByRouteAsync(
        Guid routeId,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>(
            QueryNoTracking()
                .Where(template => template.RouteId == routeId
                    && template.EffectiveFrom <= pricingAt
                    && (!template.EffectiveUntil.HasValue || pricingAt < template.EffectiveUntil.Value))
                .OrderBy(template => template.StopId)
                .ThenByDescending(template => template.EffectiveFrom)
                .ThenBy(template => template.Id)
                .ToList());
    }
}
