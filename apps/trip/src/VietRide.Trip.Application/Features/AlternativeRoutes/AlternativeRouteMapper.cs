using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

internal static class AlternativeRouteMapper
{
    public static AlternativeRouteDto ToDto(AlternativeRoute alternativeRoute, IReadOnlyList<AlternativeRouteStop> stops)
        => new(
            alternativeRoute.Id,
            alternativeRoute.RouteId,
            alternativeRoute.Name,
            alternativeRoute.Description,
            alternativeRoute.DestinationStationId,
            alternativeRoute.TotalDistanceKm,
            alternativeRoute.EstimatedDurationMinutes,
            alternativeRoute.IsActive,
            stops.OrderBy(stop => stop.OrderIndex).ThenBy(stop => stop.StopId).Select(ToDto).ToList(),
            alternativeRoute.CreatedAt,
            alternativeRoute.UpdatedAt);

    private static AlternativeRouteStopDto ToDto(AlternativeRouteStop stop)
        => new(
            stop.AlternativeRouteId,
            stop.StopId,
            stop.OrderIndex,
            stop.EstimatedDurationFromOriginMinutes,
            stop.DistanceFromOriginKm,
            stop.CreatedAt,
            stop.UpdatedAt);
}
