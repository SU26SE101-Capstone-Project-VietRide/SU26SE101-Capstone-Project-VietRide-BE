using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class ListOperatorStationsHandler(
    IOperatorStationRepository mappings,
    IStationRepository stations) : IRequestHandler<ListOperatorStationsQuery, PagedResult<OperatorStationDto>>
{
    public Task<PagedResult<OperatorStationDto>> Handle(ListOperatorStationsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? 1;
        var size = Math.Min(request.PageSize ?? 20, 100);
        var query = mappings.QueryNoTracking().Where(x => x.OperatorId == request.OperatorId);
        var all = query.OrderBy(x => x.Id).ToList();
        var ids = all.Select(x => x.StationId).ToArray();
        var stationQuery = string.IsNullOrWhiteSpace(request.Search)
            ? stations.QueryNoTracking()
            : stations.SearchByTextNoTracking(request.Search, includeLocationSnapshots: false);
        var map = stationQuery
            .Where(x => ids.Contains(x.Id))
            .ToList()
            .ToDictionary(x => x.Id, StationMapper.ToDto);
        var matchingMappings = all.Where(x => map.ContainsKey(x.StationId)).ToList();
        var items = matchingMappings
            .Skip((page - 1) * size)
            .Take(size)
            .Select(x => new OperatorStationDto(
                x.Id,
                x.OperatorId,
                x.StationId,
                map[x.StationId],
                x.DisplayNameOverride,
                x.CounterLocation,
                x.ContactPhone,
                x.Instructions,
                x.IsActive,
                x.CreatedAt,
                x.UpdatedAt))
            .ToList();
        var total = matchingMappings.Count;
        return Task.FromResult(PagedResult<OperatorStationDto>.Create(items, page, size, total));
    }
}
