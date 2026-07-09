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
    private readonly IStopRepository stopRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripStopRepository tripStopRepository;

    public GetTripRouteGeometryTrackingHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        ITripStopRepository tripStopRepository,
        IStopRepository stopRepository)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.tripStopRepository = tripStopRepository;
        this.stopRepository = stopRepository;
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

        IReadOnlyList<RouteGeometryPointDto> points;
        if (route.PathPolyline is not null)
        {
            try
            {
                points = PolylineCodec.Decode(route.PathPolyline)
                    .Select(point => new RouteGeometryPointDto(point.Latitude, point.Longitude))
                    .ToArray();
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip route geometry is invalid.");
            }
        }
        else
        {
            var tripStops = await tripStopRepository.QueryNoTracking()
                .Where(stop => stop.TripId == request.TripId)
                .OrderBy(stop => stop.OrderIndex)
                .ToArrayAsync(cancellationToken);
            var stopIds = tripStops.Select(stop => stop.StopId).ToArray();
            var stopsById = await stopRepository.QueryNoTracking()
                .Where(stop => stopIds.Contains(stop.Id))
                .ToDictionaryAsync(stop => stop.Id, cancellationToken);

            points = tripStops.Select(stop =>
            {
                if (!stopsById.TryGetValue(stop.StopId, out var routeStop))
                {
                    throw new CodedNotFoundException("STOP_NOT_FOUND", "Trip stop snapshot was not found.");
                }

                return new RouteGeometryPointDto((double)routeStop.Latitude, (double)routeStop.Longitude);
            }).ToArray();
        }

        return new TripRouteGeometryTrackingResponse(request.TripId, points, null);
    }
}
