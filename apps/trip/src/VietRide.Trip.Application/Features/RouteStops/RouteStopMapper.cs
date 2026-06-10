using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.RouteStops;

internal static class RouteStopMapper
{
    public static RouteStopDto ToDto(RouteStop routeStop)
        => new(
            routeStop.RouteId,
            routeStop.StopId,
            routeStop.OrderIndex,
            routeStop.EstimatedDurationFromOriginMinutes,
            routeStop.DistanceFromOriginKm,
            routeStop.AllowPickup,
            routeStop.AllowDropoff,
            routeStop.CreatedAt,
            routeStop.UpdatedAt);
}
