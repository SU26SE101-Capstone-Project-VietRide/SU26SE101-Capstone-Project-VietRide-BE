using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class ListRoutesHandler : IRequestHandler<ListRoutesQuery, PagedResult<RouteDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IRouteRepository routeRepository;

    public ListRoutesHandler(IRouteRepository routeRepository)
    {
        this.routeRepository = routeRepository;
    }

    public Task<PagedResult<RouteDto>> Handle(ListRoutesQuery request, CancellationToken cancellationToken)
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
        var items = query
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(RouteMapper.ToDto)
            .ToList();

        return Task.FromResult(PagedResult<RouteDto>.Create(items, page, pageSize, totalItems));
    }
}
