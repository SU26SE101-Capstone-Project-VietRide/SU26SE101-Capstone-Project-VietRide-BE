using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Application.Features.Stations;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class ListRoutesHandler : IRequestHandler<ListRoutesQuery, PagedResult<RouteListItemDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IRouteRepository routeRepository;
    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IStationRepository? stationRepository;

    public ListRoutesHandler(
        IRouteRepository routeRepository,
        IDriverScheduleRepository driverScheduleRepository,
        IStationRepository? stationRepository = null)
    {
        this.routeRepository = routeRepository;
        this.driverScheduleRepository = driverScheduleRepository;
        this.stationRepository = stationRepository;
    }

    public async Task<PagedResult<RouteListItemDto>> Handle(ListRoutesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? DefaultPage;
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        if (page < 1 || pageSize < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.Search?.Trim().Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        var query = routeRepository.QueryNoTracking()
            .Where(route => route.OperatorId == request.OperatorId && route.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim().ToLowerInvariant();
            query = query.Where(route => route.Name.ToLower().Contains(normalizedSearch));
        }

        if (request.IsActive.HasValue)
            query = query.Where(route => route.IsActive == request.IsActive.Value);

        if (request.OriginStationId.HasValue)
            query = query.Where(route => route.OriginStationId == request.OriginStationId.Value);
        if (request.DestinationStationId.HasValue)
            query = query.Where(route => route.DestinationStationId == request.DestinationStationId.Value);

        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "name" : request.SortBy.Trim();
        if (sortBy is not ("name" or "totalDistanceKm" or "estimatedDurationMinutes"))
            throw new BadRequestException("INVALID_SORT_FIELD", "Unsupported route sort field.");
        var sortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "asc" : request.SortDir.Trim();
        if (sortDir is not ("asc" or "desc"))
            throw new CodedValidationException("VALIDATION_ERROR", "sortDir must be asc or desc.");
        var descending = sortDir == "desc";

        var totalItems = query.LongCount();
        var ordered = sortBy switch
        {
            "totalDistanceKm" => descending ? query.OrderByDescending(route => route.TotalDistanceKm) : query.OrderBy(route => route.TotalDistanceKm),
            "estimatedDurationMinutes" => descending ? query.OrderByDescending(route => route.EstimatedDurationMinutes) : query.OrderBy(route => route.EstimatedDurationMinutes),
            _ => descending ? query.OrderByDescending(route => route.Name) : query.OrderBy(route => route.Name),
        };
        ordered = descending ? ordered.ThenByDescending(route => route.Id) : ordered.ThenBy(route => route.Id);
        var routes = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var routeIds = routes.Select(route => route.Id).ToArray();
        var schedules = routeIds.Length == 0
            ? []
            : await driverScheduleRepository
                .ListByRouteIdsAsync(request.OperatorId, routeIds, cancellationToken)
                .ConfigureAwait(false);
        var schedulesByRoute = schedules
            .Select(schedule =>
            {
                var dto = DriverScheduleMapper.ToDto(schedule);
                return new
                {
                    schedule.RouteId,
                    Schedule = new RouteDepartureScheduleDto(
                        dto.Id,
                        dto.DayOfWeek,
                        dto.DepartureTime,
                        dto.ValidFrom,
                        dto.ValidUntil,
                        dto.IsActive),
                };
            })
            .OrderBy(item => item.Schedule.DepartureTime)
            .ThenBy(item => item.Schedule.ValidFrom)
            .ThenBy(item => item.Schedule.Id)
            .GroupBy(item => item.RouteId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<RouteDepartureScheduleDto>)group.Select(item => item.Schedule).ToList());

        IReadOnlyCollection<RouteDepartureScheduleDto> GetSchedules(Guid routeId) =>
            schedulesByRoute.GetValueOrDefault(routeId) ?? [];

        if (stationRepository is null)
        {
            return PagedResult<RouteListItemDto>.Create(
                routes.Select(route => RouteMapper.ToListItemDto(route, GetSchedules(route.Id))).ToList(),
                page,
                pageSize,
                totalItems);
        }

        var stationIds = routes.SelectMany(x => new[] { x.OriginStationId, x.DestinationStationId }).Distinct().ToArray();
        var stations = stationRepository.QueryNoTracking().Where(x => stationIds.Contains(x.Id)).ToList()
            .ToDictionary(x => x.Id, StationMapper.ToDto);
        var items = routes.Select(route => RouteMapper.ToListItemDto(
            route,
            GetSchedules(route.Id),
            stations[route.OriginStationId],
            stations[route.DestinationStationId])).ToList();
        return PagedResult<RouteListItemDto>.Create(items, page, pageSize, totalItems);
    }

}
