using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class ListAlternativeRoutesHandler : IRequestHandler<ListAlternativeRoutesQuery, PagedResult<AlternativeRouteListItemDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IAlternativeRouteRepository alternativeRouteRepository;
    private readonly IRouteRepository routeRepository;

    public ListAlternativeRoutesHandler(
        IAlternativeRouteRepository alternativeRouteRepository,
        IRouteRepository routeRepository)
    {
        this.alternativeRouteRepository = alternativeRouteRepository;
        this.routeRepository = routeRepository;
    }

    public async Task<PagedResult<AlternativeRouteListItemDto>> Handle(ListAlternativeRoutesQuery request, CancellationToken cancellationToken)
    {
        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        var page = request.Page ?? DefaultPage;
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        var query = alternativeRouteRepository.QueryNoTracking()
            .Where(alternativeRoute => alternativeRoute.RouteId == request.RouteId);

        var totalItems = query.LongCount();
        var alternativeRoutes = query
            .OrderByDescending(alternativeRoute => alternativeRoute.IsActive)
            .ThenBy(alternativeRoute => alternativeRoute.Name)
            .ThenBy(alternativeRoute => alternativeRoute.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var items = new List<AlternativeRouteListItemDto>(alternativeRoutes.Count);
        foreach (var alternativeRoute in alternativeRoutes)
        {
            var stops = await alternativeRouteRepository.ListStopsAsync(alternativeRoute.Id, cancellationToken);
            items.Add(AlternativeRouteMapper.ToListItemDto(alternativeRoute, stops));
        }

        return PagedResult<AlternativeRouteListItemDto>.Create(items, page, pageSize, totalItems);
    }
}
