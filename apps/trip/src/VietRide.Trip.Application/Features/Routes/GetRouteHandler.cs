using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stations;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class GetRouteHandler : IRequestHandler<GetRouteQuery, RouteDto>
{
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository? stationRepository;

    public GetRouteHandler(IRouteRepository routeRepository, IStationRepository? stationRepository = null)
    {
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
    }

    public async Task<RouteDto> Handle(GetRouteQuery request, CancellationToken cancellationToken)
    {
        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        if (stationRepository is null) return RouteMapper.ToDto(route);
        var stations = stationRepository.QueryNoTracking().Where(x => x.Id == route.OriginStationId || x.Id == route.DestinationStationId)
            .ToList().ToDictionary(x => x.Id, StationMapper.ToDto);
        if (!stations.TryGetValue(route.OriginStationId, out var origin) || !stations.TryGetValue(route.DestinationStationId, out var destination))
            throw new CodedNotFoundException("STATION_NOT_FOUND", "A route station was not found.");
        return RouteMapper.ToDto(route, origin, destination);
    }
}
