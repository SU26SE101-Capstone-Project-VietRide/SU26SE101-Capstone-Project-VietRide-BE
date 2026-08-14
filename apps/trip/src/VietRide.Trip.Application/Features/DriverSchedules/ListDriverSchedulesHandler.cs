using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Routes;
using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Application.Features.Trips.Operations;
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
        if (page < 1 || pageSize < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.Search?.Trim().Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        var query = repository.QueryNoTracking().Where(x => x.OperatorId == request.OperatorId);
        if (request.RouteId.HasValue) query = query.Where(x => x.RouteId == request.RouteId.Value);
        if (request.DriverUserId.HasValue) query = query.Where(x => x.DriverUserId == request.DriverUserId.Value);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        if (request.AssistantUserId.HasValue) query = query.Where(x => x.AssistantUserId == request.AssistantUserId.Value);
        if (request.DayOfWeek is < 1 or > 7)
            throw new CodedValidationException("VALIDATION_ERROR", "dayOfWeek must be between 1 and 7.");
        if (request.DepartureFrom.HasValue && request.DepartureTo.HasValue && request.DepartureFrom > request.DepartureTo)
            throw new CodedValidationException("VALIDATION_ERROR", "departureFrom must be on or before departureTo.");
        if (request.DayOfWeek.HasValue)
            query = query.Where(x => EF.Functions.JsonContains(x.DayOfWeek, $"[{request.DayOfWeek.Value}]"));
        if (request.DepartureFrom.HasValue) query = query.Where(x => x.DepartureTime >= request.DepartureFrom.Value);
        if (request.DepartureTo.HasValue) query = query.Where(x => x.DepartureTime <= request.DepartureTo.Value);
        if (request.EffectiveAt.HasValue)
            query = query.Where(x => x.ValidFrom <= request.EffectiveAt.Value
                && (!x.ValidUntil.HasValue || x.ValidUntil >= request.EffectiveAt.Value));
        if (request.VehicleTypeId.HasValue)
        {
            var vehicleTypeMatchIds = vehicleRepository.QueryNoTracking()
                .Where(vehicle => vehicle.OperatorId == request.OperatorId
                    && vehicle.VehicleTypeId == request.VehicleTypeId.Value)
                .Select(vehicle => vehicle.Id);
            query = query.Where(schedule => schedule.VehicleId.HasValue
                && vehicleTypeMatchIds.Contains(schedule.VehicleId.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            var normalizedSearch = search.ToLowerInvariant();
            var crewSearch = await identityClient.SearchOperatorCrewAsync(
                request.OperatorId,
                search,
                cancellationToken);
            if (!crewSearch.Succeeded)
            {
                throw new TripIdentityUnavailableException(
                    crewSearch.Message ?? "Identity crew search is unavailable.");
            }

            var crewUserIds = crewSearch.Users.Select(user => user.UserId).ToArray();
            var routeIds = routeRepository.QueryNoTracking()
                .Where(route => route.OperatorId == request.OperatorId
                    && route.Name.ToLower().Contains(normalizedSearch))
                .Select(route => route.Id);
            var licensePlateMatchIds = vehicleRepository.QueryNoTracking()
                .Where(vehicle => vehicle.OperatorId == request.OperatorId
                    && vehicle.LicensePlate.ToLower().Contains(normalizedSearch))
                .Select(vehicle => vehicle.Id);
            query = query.Where(schedule =>
                routeIds.Contains(schedule.RouteId)
                || (schedule.VehicleId.HasValue && licensePlateMatchIds.Contains(schedule.VehicleId.Value))
                || crewUserIds.Contains(schedule.DriverUserId)
                || (schedule.AssistantUserId.HasValue && crewUserIds.Contains(schedule.AssistantUserId.Value)));
        }
        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "departureTime" : request.SortBy.Trim();
        if (sortBy is not ("departureTime" or "effectiveFrom"))
            throw new BadRequestException("INVALID_SORT_FIELD", "sortBy must be departureTime or effectiveFrom.");
        var sortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "asc" : request.SortDir.Trim();
        if (sortDir is not ("asc" or "desc"))
            throw new CodedValidationException("VALIDATION_ERROR", "sortDir must be asc or desc.");
        var descending = sortDir == "desc";
        var total = query.LongCount();
        var ordered = sortBy == "effectiveFrom"
            ? descending ? query.OrderByDescending(x => x.ValidFrom) : query.OrderBy(x => x.ValidFrom)
            : descending ? query.OrderByDescending(x => x.DepartureTime) : query.OrderBy(x => x.DepartureTime);
        ordered = descending ? ordered.ThenByDescending(x => x.Id) : ordered.ThenBy(x => x.Id);
        var schedules = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
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
                var layout = SeatLayoutJsonSerializer.Deserialize(x.SeatLayoutJson);
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
        var items = schedules.Select(x => new DriverScheduleDetailDto(x.Id, x.OperatorId, x.RouteId, x.VehicleId, x.DriverUserId, x.AssistantUserId, DriverScheduleMapper.ToDto(x).DayOfWeek, x.DepartureTime, x.ValidFrom, x.ValidUntil, x.IsActive, x.CreatedAt, x.UpdatedAt, routeDtos.GetValueOrDefault(x.RouteId), x.VehicleId is { } vehicleId ? vehicles.GetValueOrDefault(vehicleId) : null, users.GetValueOrDefault(x.DriverUserId), x.AssistantUserId is { } assistantId ? users.GetValueOrDefault(assistantId) : null, x.BaseFare?.Amount)).ToList();
        return PagedResult<DriverScheduleDetailDto>.Create(items, page, pageSize, total);
    }
}
