using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Common.Geometry;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed class GetTripRouteGeometryTrackingHandler
    : IRequestHandler<GetTripRouteGeometryTrackingQuery, TripRouteGeometryTrackingResponse>
{
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly IStopRepository stopRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripStopRepository tripStopRepository;

    public GetTripRouteGeometryTrackingHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        ITripStopRepository tripStopRepository,
        IStopRepository stopRepository,
        IStationRepository stationRepository)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.tripStopRepository = tripStopRepository;
        this.stopRepository = stopRepository;
        this.stationRepository = stationRepository;
    }

    public async Task<TripRouteGeometryTrackingResponse> Handle(
        GetTripRouteGeometryTrackingQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await tripRepository.QueryNoTracking()
            .FirstOrDefaultAsync(trip => trip.Id == request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var route = await routeRepository.QueryNoTracking()
            .FirstOrDefaultAsync(route => route.Id == trip.RouteId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip route was not found.");

        var tripStops = await tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == request.TripId)
            .OrderBy(stop => stop.OrderIndex)
            .ToArrayAsync(cancellationToken);
        var stopIds = tripStops.Select(stop => stop.StopId).ToArray();
        var stopsById = await stopRepository.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionaryAsync(stop => stop.Id, cancellationToken);
        var intermediateStops = tripStops.Select(stop =>
        {
            if (!stopsById.TryGetValue(stop.StopId, out var routeStop))
            {
                throw new CodedNotFoundException("STOP_NOT_FOUND", "Trip stop snapshot was not found.");
            }

            return new TripRouteIntermediateStopTrackingDto(
                routeStop.Id,
                routeStop.Name,
                stop.OrderIndex,
                (double)routeStop.Latitude,
                (double)routeStop.Longitude);
        }).ToArray();

        var geometrySource = TripRouteGeometrySources.StopsOnly;
        IReadOnlyList<RouteGeometryPointDto> points = intermediateStops
            .Select(stop => new RouteGeometryPointDto(stop.Latitude, stop.Longitude))
            .ToArray();
        if (!string.IsNullOrWhiteSpace(route.PathPolyline))
        {
            try
            {
                var decoded = PolylineCodec.Decode(route.PathPolyline)
                    .Select(point => new RouteGeometryPointDto(point.Latitude, point.Longitude))
                    .ToArray();
                if (decoded.Length >= 2)
                {
                    points = decoded;
                    geometrySource = TripRouteGeometrySources.RoutePolyline;
                }
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                geometrySource = TripRouteGeometrySources.StopsOnly;
            }
        }

        var stationIds = new[] { route.OriginStationId, route.DestinationStationId };
        var stationsById = await stationRepository.QueryNoTracking()
            .Where(station => stationIds.Contains(station.Id))
            .ToDictionaryAsync(station => station.Id, cancellationToken);

        return new TripRouteGeometryTrackingResponse(
            request.TripId,
            points,
            null,
            geometrySource,
            MapStation(stationsById.GetValueOrDefault(route.OriginStationId)),
            intermediateStops,
            MapStation(stationsById.GetValueOrDefault(route.DestinationStationId)));
    }

    private static TripRouteStationTrackingDto? MapStation(Domain.Entities.Station? station)
        => station?.Latitude is decimal latitude && station.Longitude is decimal longitude
            ? new TripRouteStationTrackingDto(
                station.Id,
                station.Name,
                (double)latitude,
                (double)longitude)
            : null;
}
