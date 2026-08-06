using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stations;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class GetRouteHandler : IRequestHandler<GetRouteQuery, RouteDto>
{
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository? stationRepository;
    private readonly IRouteStopRepository? routeStopRepository;
    private readonly IStopRepository? stopRepository;

    public GetRouteHandler(
        IRouteRepository routeRepository,
        IStationRepository? stationRepository = null,
        IRouteStopRepository? routeStopRepository = null,
        IStopRepository? stopRepository = null)
    {
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.routeStopRepository = routeStopRepository;
        this.stopRepository = stopRepository;
    }

    public async Task<RouteDto> Handle(GetRouteQuery request, CancellationToken cancellationToken)
    {
        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        return RouteDetailsProjector.Project(route, stationRepository, routeStopRepository, stopRepository);
    }
}
