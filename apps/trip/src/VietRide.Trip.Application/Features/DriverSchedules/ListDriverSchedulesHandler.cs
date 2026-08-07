using System.Text.Json;
using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Routes;
using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class ListDriverSchedulesHandler(
    IDriverScheduleRepository repository,
    IRouteRepository routeRepository,
    IVehicleRepository vehicleRepository,
    IStationRepository stationRepository,
    IIdentityInternalClient identityClient)
    : IRequestHandler<ListDriverSchedulesQuery, PagedResult<DriverScheduleDetailDto>>
{
    public async Task<PagedResult<DriverScheduleDetailDto>> Handle(ListDriverSchedulesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? 1;
        var pageSize = Math.Min(request.PageSize ?? 20, 100);
        var query = repository.QueryNoTracking().Where(x => x.OperatorId == request.OperatorId);
        if (request.RouteId.HasValue) query = query.Where(x => x.RouteId == request.RouteId.Value);
        if (request.DriverUserId.HasValue) query = query.Where(x => x.DriverUserId == request.DriverUserId.Value);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        var total = query.LongCount();
        var schedules = query.OrderBy(x => x.DepartureTime).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var routes = routeRepository.QueryNoTracking().Where(x => x.OperatorId == request.OperatorId && schedules.Select(s => s.RouteId).Contains(x.Id)).ToList();
        var stationIds = routes.SelectMany(x => new[] { x.OriginStationId, x.DestinationStationId }).Distinct().ToArray();
        var stations = stationRepository.QueryNoTracking().Where(x => stationIds.Contains(x.Id)).ToList().ToDictionary(x => x.Id, StationMapper.ToDto);
        var routeDtos = routes.Where(x => stations.ContainsKey(x.OriginStationId) && stations.ContainsKey(x.DestinationStationId)).ToDictionary(x => x.Id, x => RouteMapper.ToDto(x, stations[x.OriginStationId], stations[x.DestinationStationId]));
        var vehicleIds = schedules.Where(s => s.VehicleId.HasValue).Select(s => s.VehicleId!.Value).ToArray();
        var vehicles = vehicleRepository.QueryNoTracking().Where(x => x.OperatorId == request.OperatorId && vehicleIds.Contains(x.Id))
            .Select(x => new { x.Id, x.OperatorId, x.VehicleTypeId, x.LicensePlate, x.SeatLayoutJson, x.TotalSeats, x.MaxCargoWeightKg, x.MaxCargoVolumeM3, x.Status, x.IsActive, x.CreatedAt, x.UpdatedAt })
            .AsEnumerable()
            .ToDictionary(x => x.Id, x =>
            {
                var layout = x.SeatLayoutJson.Deserialize<SeatLayoutDto>()!;
                return new VehicleDto(
                    x.Id,
                    x.OperatorId,
                    x.VehicleTypeId,
                    x.LicensePlate,
                    layout,
                    x.TotalSeats,
                    SeatLayoutMetrics.CountUsablePassengerSeats(layout),
                    x.MaxCargoWeightKg,
                    x.MaxCargoVolumeM3,
                    null,
                    (VehicleStatusDto)x.Status,
                    x.IsActive,
                    x.CreatedAt,
                    x.UpdatedAt);
            });
        var userIds = schedules.SelectMany(x => x.AssistantUserId.HasValue ? new[] { x.DriverUserId, x.AssistantUserId.Value } : new[] { x.DriverUserId }).Distinct().ToArray();
        var users = await identityClient.GetUsersAsync(userIds, cancellationToken);
        var items = schedules.Select(x => new DriverScheduleDetailDto(x.Id, x.OperatorId, x.RouteId, x.VehicleId, x.DriverUserId, x.AssistantUserId, DriverScheduleMapper.ToDto(x).DayOfWeek, x.DepartureTime, x.ValidFrom, x.ValidUntil, x.IsActive, x.CreatedAt, x.UpdatedAt, routeDtos.GetValueOrDefault(x.RouteId), x.VehicleId is { } vehicleId ? vehicles.GetValueOrDefault(vehicleId) : null, users.GetValueOrDefault(x.DriverUserId), x.AssistantUserId is { } assistantId ? users.GetValueOrDefault(assistantId) : null)).ToList();
        return PagedResult<DriverScheduleDetailDto>.Create(items, page, pageSize, total);
    }
}
