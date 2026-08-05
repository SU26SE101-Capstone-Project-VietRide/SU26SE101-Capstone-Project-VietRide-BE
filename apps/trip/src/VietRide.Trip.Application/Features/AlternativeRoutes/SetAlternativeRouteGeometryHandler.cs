using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Common.Geometry;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class SetAlternativeRouteGeometryHandler
    : IRequestHandler<SetAlternativeRouteGeometryCommand, AlternativeRouteDto>
{
    private readonly IAlternativeRouteRepository alternativeRouteRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly IRouteChangeProposalLifecycleService? routeChangeProposals;
    private readonly IClock? clock;

    public SetAlternativeRouteGeometryHandler(
        IAlternativeRouteRepository alternativeRouteRepository,
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork,
        IRouteChangeProposalLifecycleService? routeChangeProposals = null,
        IClock? clock = null)
    {
        this.alternativeRouteRepository = alternativeRouteRepository;
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
        this.routeChangeProposals = routeChangeProposals;
        this.clock = clock;
    }

    public async Task<AlternativeRouteDto> Handle(
        SetAlternativeRouteGeometryCommand request,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        var alternativeRoute = await alternativeRouteRepository.GetOwnedByIdAsync(
            request.OperatorId,
            request.AlternativeRouteId,
            cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
        var route = await routeRepository.GetOwnedByIdAsync(
            request.OperatorId,
            alternativeRoute.RouteId,
            cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        var routeStops = await alternativeRouteRepository.ListStopsAsync(alternativeRoute.Id, cancellationToken);

        if (request.PathPolyline is not null)
        {
            var polyline = RouteGeometryValidator.DecodeAndValidate(request.PathPolyline);
            var stopIds = routeStops.Select(routeStop => routeStop.StopId).ToArray();
            var stops = await stopRepository.QueryNoTracking()
                .Where(stop => stopIds.Contains(stop.Id))
                .Select(stop => new { stop.Id, stop.Latitude, stop.Longitude })
                .ToArrayAsync(cancellationToken);
            var stationIds = new[] { route.OriginStationId, alternativeRoute.DestinationStationId };
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

        alternativeRoute.SetPathGeometry(request.PathPolyline);
        alternativeRouteRepository.Update(alternativeRoute);
        if (routeChangeProposals is not null)
            await routeChangeProposals.ExpirePendingForSourceAsync(alternativeRoute.Id, clock?.UtcNow ?? DateTimeOffset.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AlternativeRouteMapper.ToDto(alternativeRoute, routeStops);
    }
}
