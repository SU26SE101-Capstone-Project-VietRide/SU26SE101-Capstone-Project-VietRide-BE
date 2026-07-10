using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stations;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class ListRoutesHandler : IRequestHandler<ListRoutesQuery, PagedResult<RouteListItemDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository? stationRepository;

    public ListRoutesHandler(IRouteRepository routeRepository, IStationRepository? stationRepository = null)
    {
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
    }

    public Task<PagedResult<RouteListItemDto>> Handle(ListRoutesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? DefaultPage;
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        var query = routeRepository.QueryNoTracking()
            .Where(route => route.OperatorId == request.OperatorId && route.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(route => route.Name.Contains(search));
        }

        var totalItems = query.LongCount();
        var routes = query
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        if (stationRepository is null) return Task.FromResult(PagedResult<RouteListItemDto>.Create(routes.Select(route => RouteMapper.ToListItemDto(route)).ToList(), page, pageSize, totalItems));
        var stationIds = routes.SelectMany(x => new[] { x.OriginStationId, x.DestinationStationId }).Distinct().ToArray();
        var stations = stationRepository.QueryNoTracking().Where(x => stationIds.Contains(x.Id)).ToList()
            .ToDictionary(x => x.Id, StationMapper.ToDto);
        var items = routes.Select(route => RouteMapper.ToListItemDto(route, stations[route.OriginStationId], stations[route.DestinationStationId])).ToList();
        return Task.FromResult(PagedResult<RouteListItemDto>.Create(items, page, pageSize, totalItems));
    }
}
