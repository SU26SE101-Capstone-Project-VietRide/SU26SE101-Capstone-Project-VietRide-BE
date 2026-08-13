using MediatR;
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
        var query = routeRepository.QueryNoTracking()
            .Where(route => route.OperatorId == request.OperatorId && route.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(route => route.Name.Contains(search));
        }

        if (request.IsActive.HasValue)
            query = query.Where(route => route.IsActive == request.IsActive.Value);

        var totalItems = query.LongCount();
        var routes = query
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
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
