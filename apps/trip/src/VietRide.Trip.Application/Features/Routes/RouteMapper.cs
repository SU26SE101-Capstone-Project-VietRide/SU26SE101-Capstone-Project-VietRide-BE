using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Routes;

internal static class RouteMapper
{
    public static RouteListItemDto ToListItemDto(Route route, StationDto? originStation = null, StationDto? destinationStation = null)
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
            route.UpdatedAt,
            originStation,
            destinationStation);

    public static RouteDto ToDto(Route route, StationDto? originStation = null, StationDto? destinationStation = null)
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
            route.PathPolyline,
            route.IsActive,
            route.CreatedAt,
            route.UpdatedAt,
            originStation,
            destinationStation);
}
