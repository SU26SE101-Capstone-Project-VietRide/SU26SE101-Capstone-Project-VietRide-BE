using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Routes;

internal static class RouteMapper
{
    public static RouteDto ToDto(Route route)
        => new(
            route.Id,
            route.OperatorId,
            route.Name,
            route.OriginStationId,
            route.DestinationStationId,
            route.ReturnRouteId,
            route.BaseFare.Amount,
            route.TotalDistanceKm,
            route.EstimatedDurationMinutes,
            route.IsActive,
            route.CreatedAt,
            route.UpdatedAt);
}
