using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Routes;

internal static class RouteDetailsProjector
{
    public static RouteDto Project(
        Route route,
        IStationRepository? stationRepository,
        IRouteStopRepository? routeStopRepository,
        IStopRepository? stopRepository)
    {
        StationDto? origin = null;
        StationDto? destination = null;
        if (stationRepository is not null)
        {
            var stations = stationRepository.QueryNoTracking()
                .Where(station => station.Id == route.OriginStationId || station.Id == route.DestinationStationId)
                .ToDictionary(station => station.Id, StationMapper.ToDto);
            if (!stations.TryGetValue(route.OriginStationId, out origin)
                || !stations.TryGetValue(route.DestinationStationId, out destination))
            {
                throw new CodedNotFoundException("STATION_NOT_FOUND", "A route station was not found.");
            }
        }

        IReadOnlyList<RouteMapStopDto> stops = [];
        if (routeStopRepository is not null && stopRepository is not null)
        {
            var routeStops = routeStopRepository.QueryNoTracking()
                .Where(routeStop => routeStop.RouteId == route.Id)
                .OrderBy(routeStop => routeStop.OrderIndex)
                .ThenBy(routeStop => routeStop.StopId)
                .ToArray();
            var stopIds = routeStops.Select(routeStop => routeStop.StopId).ToArray();
            var stopById = stopRepository.QueryNoTracking()
                .Where(stop => stopIds.Contains(stop.Id))
                .ToDictionary(stop => stop.Id);
            stops = routeStops.Select(routeStop =>
            {
                if (!stopById.TryGetValue(routeStop.StopId, out var stop))
                    throw new CodedNotFoundException("STOP_NOT_FOUND", "A route stop was not found.");

                return new RouteMapStopDto(
                    routeStop.RouteId,
                    routeStop.StopId,
                    routeStop.OrderIndex,
                    routeStop.EstimatedDurationFromOriginMinutes,
                    routeStop.DistanceFromOriginKm,
                    routeStop.AllowPickup,
                    routeStop.AllowDropoff,
                    stop.Name,
                    stop.Address,
                    stop.Latitude,
                    stop.Longitude,
                    stop.IsActive,
                    routeStop.CreatedAt,
                    routeStop.UpdatedAt);
            }).ToArray();
        }

        return RouteMapper.ToDto(route, origin, destination, stops);
    }
}
