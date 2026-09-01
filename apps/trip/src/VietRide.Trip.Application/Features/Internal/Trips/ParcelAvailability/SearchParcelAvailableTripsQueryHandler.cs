using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;

public sealed class SearchParcelAvailableTripsQueryHandler
    : IRequestHandler<SearchParcelAvailableTripsQuery, PagedResult<ParcelTripAvailabilityItemDto>>
{
    private readonly IIdentityInternalClient _identityClient;
    private readonly ILocationRepository _locationRepository;
    private readonly IRouteRepository _routeRepository;
    private readonly IStationRepository _stationRepository;
    private readonly IStopRepository _stopRepository;
    private readonly ITripRepository _tripRepository;
    private readonly ITripStopRepository _tripStopRepository;

    public SearchParcelAvailableTripsQueryHandler(
        IRouteRepository routeRepository,
        ITripRepository tripRepository,
        IStationRepository stationRepository,
        IIdentityInternalClient identityClient,
        ITripStopRepository tripStopRepository,
        IStopRepository stopRepository,
        ILocationRepository locationRepository)
    {
        _routeRepository = routeRepository;
        _tripRepository = tripRepository;
        _stationRepository = stationRepository;
        _identityClient = identityClient;
        _tripStopRepository = tripStopRepository;
        _stopRepository = stopRepository;
        _locationRepository = locationRepository;
    }

    public async Task<PagedResult<ParcelTripAvailabilityItemDto>> Handle(
        SearchParcelAvailableTripsQuery request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var destinationMode = GetDestinationMode(request);
        var destinationLocationIds = destinationMode == DestinationMode.Location
            ? await ResolveDestinationLocationIdsAsync(request, cancellationToken)
            : null;
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var range = BusinessTime.GetUtcDayRange(request.DepartureDate);

        if (request.EligibleRouteIds is { Count: 0 })
        {
            return PagedResult<ParcelTripAvailabilityItemDto>.Create([], page, pageSize, 0);
        }

        var eligibleRouteIds = request.EligibleRouteIds?
            .Where(routeId => routeId != Guid.Empty)
            .Distinct()
            .ToArray();

        var routeQuery = _routeRepository.QueryNoTracking()
            .Where(route => route.OriginStationId == request.OriginStationId
                && route.DeletedAt == null
                && route.IsActive);
        if (destinationMode == DestinationMode.Station)
        {
            routeQuery = routeQuery.Where(route => route.DestinationStationId == request.DestinationStationId);
        }

        if (eligibleRouteIds is not null)
        {
            routeQuery = routeQuery.Where(route => eligibleRouteIds.Contains(route.Id));
        }

        var routes = await routeQuery
            .ToDictionaryAsync(route => route.Id, cancellationToken)
            .ConfigureAwait(false);

        if (routes.Count == 0)
        {
            return PagedResult<ParcelTripAvailabilityItemDto>.Create([], page, pageSize, 0);
        }

        var stationIds = routes.Values
            .SelectMany(route => new[] { route.OriginStationId, route.DestinationStationId })
            .Distinct()
            .ToArray();
        var stations = await _stationRepository.QueryNoTracking()
            .Where(station => stationIds.Contains(station.Id)
                && station.IsActive
                && station.DeletedAt == null)
            .ToDictionaryAsync(station => station.Id, cancellationToken)
            .ConfigureAwait(false);

        routes = routes
            .Where(pair => stations.ContainsKey(pair.Value.OriginStationId)
                && stations.ContainsKey(pair.Value.DestinationStationId))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (routes.Count == 0)
        {
            return PagedResult<ParcelTripAvailabilityItemDto>.Create([], page, pageSize, 0);
        }

        var routeIds = routes.Keys.ToHashSet();
        var tripCandidates = await _tripRepository.QueryNoTracking()
            .Where(trip => routeIds.Contains(trip.RouteId)
                && trip.Status == TripStatus.SCHEDULED
                && trip.AssistantUserId != null
                && trip.DepartureDateTime >= range.FromUtc
                && trip.DepartureDateTime < range.ToUtcExclusive)
            .OrderBy(trip => trip.DepartureDateTime)
            .ThenBy(trip => trip.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tripIds = tripCandidates.Select(trip => trip.Id).ToArray();
        var tripStops = tripIds.Length == 0
            ? []
            : await _tripStopRepository.QueryNoTracking()
                .Where(tripStop => tripIds.Contains(tripStop.TripId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var stopIds = tripStops.Select(tripStop => tripStop.StopId).Distinct().ToArray();
        var stops = stopIds.Length == 0
            ? new Dictionary<Guid, Stop>()
            : await _stopRepository.QueryNoTracking()
                .Where(stop => stopIds.Contains(stop.Id)
                    && stop.IsActive
                    && stop.DeletedAt == null)
                .ToDictionaryAsync(stop => stop.Id, cancellationToken)
                .ConfigureAwait(false);
        var tripStopsByTrip = tripStops
            .GroupBy(tripStop => tripStop.TripId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var candidates = tripCandidates
            .Select(trip => new
            {
                Trip = trip,
                AvailableCargoWeightKg = GetAvailableCargoWeightKg(trip),
                AvailableCargoVolumeM3 = GetAvailableCargoVolumeM3(trip),
                DropoffPoints = BuildDropoffPoints(
                    trip,
                    routes[trip.RouteId],
                    stations,
                    tripStopsByTrip.GetValueOrDefault(trip.Id) ?? [],
                    stops,
                    destinationMode,
                    request.DropoffStopId,
                    destinationLocationIds),
            })
            .Where(item => item.AvailableCargoWeightKg >= request.EstimatedWeightKg
                && item.AvailableCargoVolumeM3 >= request.EstimatedVolumeM3
                && item.DropoffPoints.Count > 0)
            .ToList();

        var pageItems = candidates
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var operatorNames = await GetOperatorNamesAsync(
            pageItems.Select(item => item.Trip.OperatorId).Distinct().ToArray(),
            cancellationToken);

        var items = pageItems
            .Select(item => new ParcelTripAvailabilityItemDto(
                item.Trip.Id,
                item.Trip.RouteId,
                item.Trip.OperatorId,
                ResolveOperatorName(operatorNames, item.Trip.OperatorId),
                item.Trip.Status.ToString(),
                new ParcelTripStationDto(
                    routes[item.Trip.RouteId].OriginStationId,
                    stations[routes[item.Trip.RouteId].OriginStationId].Name),
                new ParcelTripStationDto(
                    routes[item.Trip.RouteId].DestinationStationId,
                    stations[routes[item.Trip.RouteId].DestinationStationId].Name),
                item.DropoffPoints,
                item.Trip.DepartureDateTime,
                item.Trip.EstimatedArrivalTime,
                item.AvailableCargoWeightKg,
                item.AvailableCargoVolumeM3))
            .ToList();

        return PagedResult<ParcelTripAvailabilityItemDto>.Create(items, page, pageSize, candidates.Count);
    }

    private static IReadOnlyList<ParcelTripDropoffPointDto> BuildDropoffPoints(
        Domain.Entities.Trip trip,
        Route route,
        IReadOnlyDictionary<Guid, Station> stations,
        IReadOnlyCollection<TripStop> tripStops,
        IReadOnlyDictionary<Guid, Stop> stops,
        DestinationMode destinationMode,
        Guid? requestedStopId,
        IReadOnlySet<Guid>? destinationLocationIds)
    {
        var points = new List<ParcelTripDropoffPointDto>();
        var terminalOrder = tripStops.Count == 0 ? 1 : tripStops.Max(tripStop => tripStop.OrderIndex) + 1;
        var destinationStation = stations[route.DestinationStationId];

        if (destinationMode == DestinationMode.Station
            || (destinationMode == DestinationMode.Location
                && destinationStation.LocationId.HasValue
                && destinationLocationIds!.Contains(destinationStation.LocationId.Value)))
        {
            points.Add(new ParcelTripDropoffPointDto(
                "STATION",
                destinationStation.Id,
                null,
                destinationStation.Name,
                terminalOrder,
                trip.EstimatedArrivalTime));
        }

        if (destinationMode != DestinationMode.Station)
        {
            points.AddRange(tripStops
                .Where(tripStop => tripStop.AllowDropoff
                    && stops.TryGetValue(tripStop.StopId, out var stop)
                    && (destinationMode == DestinationMode.Stop
                        ? stop.Id == requestedStopId
                        : stop.LocationId.HasValue && destinationLocationIds!.Contains(stop.LocationId.Value)))
                .Select(tripStop =>
                {
                    var stop = stops[tripStop.StopId];
                    return new ParcelTripDropoffPointDto(
                        "STOP",
                        null,
                        stop.Id,
                        stop.Name,
                        tripStop.OrderIndex,
                        tripStop.EstimatedArrivalTime);
                }));
        }

        return points
            .OrderBy(point => point.OrderIndex)
            .ThenBy(point => point.StationId ?? point.StopId ?? Guid.Empty)
            .ToArray();
    }

    private async Task<IReadOnlySet<Guid>> ResolveDestinationLocationIdsAsync(
        SearchParcelAvailableTripsQuery request,
        CancellationToken cancellationToken)
    {
        var province = await _locationRepository.GetActiveByCodeAsync(
            request.DestinationProvinceCode!.Trim(),
            cancellationToken);
        if (province is null || !Location.IsTopLevelType(province.Type) || province.ParentLocationId.HasValue)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Destination province was not found or inactive.",
                [new ValidationError(nameof(request.DestinationProvinceCode),
                    "Destination province was not found, inactive, or not top-level.")]);
        }

        if (!string.IsNullOrWhiteSpace(request.DestinationLocationCode))
        {
            var location = await _locationRepository.GetActiveByCodeAsync(
                request.DestinationLocationCode.Trim(),
                cancellationToken);
            if (location is null || !Location.IsLeafType(location.Type) || location.ParentLocationId != province.Id)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Destination location does not belong to the selected province.",
                    [new ValidationError(nameof(request.DestinationLocationCode),
                        "Destination location was not found, inactive, or is not a direct leaf of the selected province.")]);
            }

            return new HashSet<Guid> { location.Id };
        }

        var children = await _locationRepository.ListActiveChildrenAsync(province.Id, null, cancellationToken);
        return children
            .Where(location => Location.IsLeafType(location.Type))
            .Select(location => location.Id)
            .Append(province.Id)
            .ToHashSet();
    }

    private static void ValidateRequest(SearchParcelAvailableTripsQuery request)
    {
        if (request.OriginStationId == Guid.Empty)
        {
            ThrowValidation(nameof(request.OriginStationId), "originStationId must not be empty.");
        }

        if (request.EstimatedWeightKg <= 0)
        {
            ThrowValidation(nameof(request.EstimatedWeightKg), "estimatedWeightKg must be greater than 0.");
        }

        if (request.EstimatedVolumeM3 <= 0)
        {
            ThrowValidation(nameof(request.EstimatedVolumeM3), "estimatedVolumeM3 must be greater than 0.");
        }

        if (request.DestinationStationId == Guid.Empty)
        {
            ThrowValidation(nameof(request.DestinationStationId), "destinationStationId must not be empty.");
        }

        if (request.DropoffStopId == Guid.Empty)
        {
            ThrowValidation(nameof(request.DropoffStopId), "dropoffStopId must not be empty.");
        }

        var modeCount = (request.DestinationStationId.HasValue ? 1 : 0)
            + (request.DropoffStopId.HasValue ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(request.DestinationProvinceCode) ? 1 : 0);
        if (modeCount != 1)
        {
            ThrowValidation(
                nameof(request.DestinationStationId),
                "Exactly one destination mode must be supplied: destinationStationId, dropoffStopId, or destinationProvinceCode.");
        }

        if (!string.IsNullOrWhiteSpace(request.DestinationLocationCode)
            && string.IsNullOrWhiteSpace(request.DestinationProvinceCode))
        {
            ThrowValidation(
                nameof(request.DestinationLocationCode),
                "destinationLocationCode requires destinationProvinceCode.");
        }
    }

    private static DestinationMode GetDestinationMode(SearchParcelAvailableTripsQuery request)
        => request.DestinationStationId.HasValue
            ? DestinationMode.Station
            : request.DropoffStopId.HasValue
                ? DestinationMode.Stop
                : DestinationMode.Location;

    private static void ThrowValidation(string field, string message)
        => throw new CodedValidationException(
            "VALIDATION_ERROR",
            message,
            [new ValidationError(field, message)]);

    private async Task<IReadOnlyDictionary<Guid, string>> GetOperatorNamesAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var operatorId in operatorIds)
        {
            var lookup = await _identityClient.GetOperatorAsync(operatorId, cancellationToken);
            if (!lookup.Found || string.IsNullOrWhiteSpace(lookup.Name))
            {
                continue;
            }

            names[operatorId] = lookup.Name;
        }

        return names;
    }

    private static string ResolveOperatorName(IReadOnlyDictionary<Guid, string> operatorNames, Guid operatorId)
        => operatorNames.TryGetValue(operatorId, out var name)
            ? name
            : $"Operator {operatorId:N}";

    private static decimal GetAvailableCargoWeightKg(Domain.Entities.Trip trip)
    {
        if (!trip.MaxCargoWeightKg.HasValue)
        {
            return 0m;
        }

        return Math.Max(0m, trip.MaxCargoWeightKg.Value - trip.ReservedParcelWeightKg - trip.TotalLoadedWeightKg);
    }

    private static decimal GetAvailableCargoVolumeM3(Domain.Entities.Trip trip)
    {
        if (!trip.MaxCargoVolumeM3.HasValue)
        {
            return 0m;
        }

        return Math.Max(0m, trip.MaxCargoVolumeM3.Value - trip.ReservedParcelVolumeM3 - trip.TotalLoadedVolumeM3);
    }

    private enum DestinationMode
    {
        Station,
        Stop,
        Location,
    }
}
