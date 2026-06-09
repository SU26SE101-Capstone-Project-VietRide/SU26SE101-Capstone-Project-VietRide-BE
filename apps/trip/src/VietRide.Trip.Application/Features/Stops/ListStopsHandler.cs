using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class ListStopsHandler : IRequestHandler<ListStopsQuery, PagedResult<StopDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IStopRepository stopRepository;

    public ListStopsHandler(IStopRepository stopRepository)
    {
        this.stopRepository = stopRepository;
    }

    public Task<PagedResult<StopDto>> Handle(ListStopsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? DefaultPage;
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        var query = stopRepository.QueryNoTracking()
            .Where(stop => stop.OperatorId == request.OperatorId && stop.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(stop => stop.Name.Contains(search) || (stop.Address != null && stop.Address.Contains(search)));
        }

        var totalItems = query.LongCount();
        var items = query
            .OrderBy(stop => stop.Name)
            .ThenBy(stop => stop.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(StopMapper.ToDto)
            .ToList();

        return Task.FromResult(PagedResult<StopDto>.Create(items, page, pageSize, totalItems));
    }
}
