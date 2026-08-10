using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class ListAdminStopsHandler : IRequestHandler<ListAdminStopsQuery, PagedResult<StopDto>>
{
    private readonly ILocationRepository? locations;
    private readonly IStopRepository stops;

    public ListAdminStopsHandler(IStopRepository stops, ILocationRepository? locations = null)
    {
        this.stops = stops;
        this.locations = locations;
    }

    public Task<PagedResult<StopDto>> Handle(ListAdminStopsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page ?? 1, 1);
        var size = Math.Clamp(request.PageSize ?? 20, 1, 100);
        var query = string.IsNullOrWhiteSpace(request.Search)
            ? stops.QueryNoTracking()
            : stops.SearchByTextNoTracking(request.Search);
        if (request.OperatorId.HasValue) query = query.Where(x => x.OperatorId == request.OperatorId.Value);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        var total = query.Count();
        var pageStops = query.OrderBy(x => x.Name).Skip((page - 1) * size).Take(size).ToList();
        var locationContexts = StopLocationContextResolver.Resolve(locations, pageStops);
        var items = pageStops.Select(stop => StopMapper.ToDto(stop, locationContexts)).ToList();
        return Task.FromResult(PagedResult<StopDto>.Create(items, page, size, total));
    }
}
