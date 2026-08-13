using MediatR;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Routes;

public sealed class SearchInternalRoutesHandler(
    IRouteRepository routes,
    IStationRepository stations)
    : IRequestHandler<SearchInternalRoutesQuery, InternalRouteSearchDto>
{
    public Task<InternalRouteSearchDto> Handle(
        SearchInternalRoutesQuery request,
        CancellationToken cancellationToken)
    {
        var search = request.Search.Trim();
        var normalizedSearch = search.ToLowerInvariant();
        var stationIds = stations.SearchByTextNoTracking(search, includeLocationSnapshots: true)
            .Select(station => station.Id);
        var routeIds = routes.QueryNoTracking()
            .Where(route => route.OperatorId == request.OperatorId
                && route.DeletedAt == null
                && (route.Name.ToLower().Contains(normalizedSearch)
                    || stationIds.Contains(route.OriginStationId)
                    || stationIds.Contains(route.DestinationStationId)))
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
            .Select(route => route.Id)
            .ToList();
        return Task.FromResult(new InternalRouteSearchDto(routeIds));
    }
}
