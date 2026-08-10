using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class ListAdminStationsHandler(IStationRepository stations)
    : IRequestHandler<ListAdminStationsQuery, PagedResult<StationDto>>
{
    public Task<PagedResult<StationDto>> Handle(ListAdminStationsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page ?? 1, 1);
        var size = Math.Clamp(request.PageSize ?? 20, 1, 100);
        var query = string.IsNullOrWhiteSpace(request.Search)
            ? stations.QueryNoTracking()
            : stations.SearchByTextNoTracking(request.Search, includeLocationSnapshots: true);

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        var total = query.Count();
        var items = query.OrderBy(x => x.Name).Skip((page - 1) * size).Take(size)
            .AsEnumerable().Select(StationMapper.ToDto).ToList();
        return Task.FromResult(PagedResult<StationDto>.Create(items, page, size, total));
    }
}
