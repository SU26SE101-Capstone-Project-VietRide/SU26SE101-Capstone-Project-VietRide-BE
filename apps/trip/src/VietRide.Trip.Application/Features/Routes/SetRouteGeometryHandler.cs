using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Common.Geometry;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class SetRouteGeometryHandler : IRequestHandler<SetRouteGeometryCommand, RouteDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly IStationRepository stationRepository;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public SetRouteGeometryHandler(
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IRouteStopRepository routeStopRepository,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<RouteDto> Handle(SetRouteGeometryCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");

        if (request.PathPolyline is not null)
        {
            var polyline = RouteGeometryValidator.DecodeAndValidate(request.PathPolyline);
            var stopIds = await routeStopRepository.QueryNoTracking()
                .Where(routeStop => routeStop.RouteId == route.Id)
                .Select(routeStop => routeStop.StopId)
                .ToArrayAsync(cancellationToken);
            var stops = await stopRepository.QueryNoTracking()
                .Where(stop => stopIds.Contains(stop.Id))
                .Select(stop => new { stop.Id, stop.Latitude, stop.Longitude })
                .ToArrayAsync(cancellationToken);
            var stationIds = new[] { route.OriginStationId, route.DestinationStationId };
            var stations = await stationRepository.QueryNoTracking()
                .Where(station => stationIds.Contains(station.Id))
                .Select(station => new { station.Id, station.Latitude, station.Longitude })
                .ToArrayAsync(cancellationToken);

            RouteGeometryValidator.ValidateWaypoints(
                polyline,
                stops.Select(stop => (stop.Id, new GeoPoint((double)stop.Latitude, (double)stop.Longitude))),
                stations
                    .Where(station => station.Latitude.HasValue && station.Longitude.HasValue)
                    .Select(station => (station.Id, new GeoPoint((double)station.Latitude!.Value, (double)station.Longitude!.Value))));
        }

        route.SetPathGeometry(request.PathPolyline);
        routeRepository.Update(route);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RouteMapper.ToDto(route);
    }
}
