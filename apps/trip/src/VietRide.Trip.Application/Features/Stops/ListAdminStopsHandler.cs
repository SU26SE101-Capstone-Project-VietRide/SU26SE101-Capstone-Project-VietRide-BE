using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class ListAdminStopsHandler(IStopRepository stops)
    : IRequestHandler<ListAdminStopsQuery, PagedResult<StopDto>>
{
    public Task<PagedResult<StopDto>> Handle(ListAdminStopsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page ?? 1, 1);
        var size = Math.Clamp(request.PageSize ?? 20, 1, 100);
        var query = stops.QueryNoTracking();
        if (request.OperatorId.HasValue) query = query.Where(x => x.OperatorId == request.OperatorId.Value);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(search) || (x.Address != null && x.Address.ToLower().Contains(search)));
        }
        var total = query.Count();
        var items = query.OrderBy(x => x.Name).Skip((page - 1) * size).Take(size).Select(StopMapper.ToDto).ToList();
        return Task.FromResult(PagedResult<StopDto>.Create(items, page, size, total));
    }
}
