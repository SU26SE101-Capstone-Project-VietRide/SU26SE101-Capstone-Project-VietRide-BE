using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class ListOperatorStationsHandler(
    IOperatorStationRepository mappings,
    IStationRepository stations) : IRequestHandler<ListOperatorStationsQuery, PagedResult<OperatorStationDto>>
{
    public async Task<PagedResult<OperatorStationDto>> Handle(ListOperatorStationsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? 1;
        var size = Math.Min(request.PageSize ?? 20, 100);
        if (page < 1 || size < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.Search?.Trim().Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "createdAt" : request.SortBy.Trim();
        if (sortBy is not ("name" or "createdAt" or "updatedAt"))
            throw new BadRequestException("INVALID_SORT_FIELD", "sortBy must be name, createdAt or updatedAt.");
        var sortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "asc" : request.SortDir.Trim();
        if (sortDir is not ("asc" or "desc"))
            throw new CodedValidationException("VALIDATION_ERROR", "sortDir must be asc or desc.");
        var descending = sortDir == "desc";
        var stationQuery = string.IsNullOrWhiteSpace(request.Search)
            ? stations.QueryNoTracking()
            : stations.SearchByTextNoTracking(request.Search, includeLocationSnapshots: false);
        var joined = from mapping in mappings.QueryNoTracking()
                     join station in stationQuery on mapping.StationId equals station.Id
                     where mapping.OperatorId == request.OperatorId
                     select new { Mapping = mapping, Station = station };
        if (request.IsActive.HasValue) joined = joined.Where(x => x.Mapping.IsActive == request.IsActive.Value);
        if (request.SupportsShuttle.HasValue) joined = joined.Where(x => x.Station.SupportsShuttle == request.SupportsShuttle.Value);
        var total = await joined.LongCountAsync(cancellationToken);
        var ordered = sortBy switch
        {
            "name" => descending ? joined.OrderByDescending(x => x.Station.Name) : joined.OrderBy(x => x.Station.Name),
            "updatedAt" => descending ? joined.OrderByDescending(x => x.Mapping.UpdatedAt) : joined.OrderBy(x => x.Mapping.UpdatedAt),
            _ => descending ? joined.OrderByDescending(x => x.Mapping.CreatedAt) : joined.OrderBy(x => x.Mapping.CreatedAt),
        };
        ordered = descending ? ordered.ThenByDescending(x => x.Mapping.Id) : ordered.ThenBy(x => x.Mapping.Id);
        var rows = await ordered
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        var items = rows.Select(x => new OperatorStationDto(
            x.Mapping.Id, x.Mapping.OperatorId, x.Mapping.StationId, StationMapper.ToDto(x.Station),
            x.Mapping.DisplayNameOverride, x.Mapping.CounterLocation, x.Mapping.ContactPhone,
            x.Mapping.Instructions, x.Mapping.IsActive, x.Mapping.CreatedAt, x.Mapping.UpdatedAt)).ToList();
        return PagedResult<OperatorStationDto>.Create(items, page, size, total);
    }
}
