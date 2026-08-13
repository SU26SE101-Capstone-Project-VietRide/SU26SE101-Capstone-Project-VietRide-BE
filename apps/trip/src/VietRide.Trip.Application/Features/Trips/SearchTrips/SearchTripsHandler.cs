using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed class SearchTripsHandler : IRequestHandler<SearchTripsQuery, SearchTripsResult>
{
    private const int Page = 1;
    private const int PageSize = 20;

    private readonly IIdentityInternalClient identityInternalClient;
    private readonly ILocationRepository? locationRepository;
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopRepository tripStopRepository;
    private readonly IStopRepository? stopRepository;
    private readonly IFareSurchargeService? fareSurchargeService;

    public SearchTripsHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ILocationRepository locationRepository,
        IIdentityInternalClient identityInternalClient,
        IFareSurchargeService? fareSurchargeService = null,
        IStopRepository? stopRepository = null)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.locationRepository = locationRepository;
        this.identityInternalClient = identityInternalClient;
        this.fareSurchargeService = fareSurchargeService;
        this.stopRepository = stopRepository;
    }

    public SearchTripsHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        IIdentityInternalClient identityInternalClient)
        : this(
            tripRepository,
            routeRepository,
            stationRepository,
            tripSeatRepository,
            tripStopRepository,
            null!,
            identityInternalClient)
    {
    }

    public async Task<SearchTripsResult> Handle(SearchTripsQuery request, CancellationToken cancellationToken)
    {
        var range = BusinessTime.GetUtcDayRange(request.DepartureDate);
        var searchFilter = await ResolveSearchFilterAsync(request, cancellationToken);
        var routeQuery = routeRepository.QueryNoTracking()
            .Where(route => route.DeletedAt == null && route.IsActive);
        if (!searchFilter.IsHierarchyMode)
        {
            routeQuery = routeQuery.Where(route =>
                route.OriginStationId == request.OriginStationId
                && route.DestinationStationId == request.DestinationStationId);
        }

        var routes = routeQuery.ToDictionary(route => route.Id);

        if (routes.Count == 0)
        {
            return SearchTripsResult.Create([], Page, PageSize, 0);
        }

        var projectedStationIds = routes.Values
            .SelectMany(route => new[] { route.OriginStationId, route.DestinationStationId })
            .ToHashSet();
        var stationsById = stationRepository.QueryNoTracking()
            .Where(station => projectedStationIds.Contains(station.Id))
            .ToDictionary(station => station.Id);
        routes = routes.Values
            .Where(route => stationsById.ContainsKey(route.OriginStationId)
                && stationsById.ContainsKey(route.DestinationStationId)
                && stationsById[route.OriginStationId].IsActive
                && stationsById[route.OriginStationId].DeletedAt is null
                && stationsById[route.DestinationStationId].IsActive
                && stationsById[route.DestinationStationId].DeletedAt is null)
            .ToDictionary(route => route.Id);
        if (routes.Count == 0)
        {
            return SearchTripsResult.Create([], Page, PageSize, 0);
        }

        var routeIds = routes.Keys.ToHashSet();
        var candidates = tripRepository.QueryNoTracking()
            .Where(trip => routeIds.Contains(trip.RouteId)
                && trip.Status == TripStatus.SCHEDULED
                && trip.DepartureDateTime >= range.FromUtc
                && trip.DepartureDateTime < range.ToUtcExclusive)
            .OrderBy(trip => trip.DepartureDateTime)
            .ThenBy(trip => trip.Id)
            .ToList();
        var tripIds = candidates.Select(trip => trip.Id).ToHashSet();
        var seatsByTrip = tripSeatRepository.QueryNoTracking()
            .Where(seat => tripIds.Contains(seat.TripId))
            .ToArray()
            .GroupBy(seat => seat.TripId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<TripSeat>)group.ToArray());
        var stopsByTrip = tripStopRepository.QueryNoTracking()
            .Where(stop => tripIds.Contains(stop.TripId))
            .ToArray()
            .GroupBy(stop => stop.TripId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<TripStop>)group.ToArray());
        var stopIds = stopsByTrip.Values.SelectMany(stops => stops).Select(stop => stop.StopId).ToHashSet();
        var canonicalStops = stopRepository is null
            ? new Dictionary<Guid, Stop>()
            : stopRepository.QueryNoTracking()
                .Where(stop => stopIds.Contains(stop.Id) && stop.IsActive && stop.DeletedAt == null)
                .ToDictionary(stop => stop.Id);

        var filtered = candidates
            .Select(trip => new
            {
                Trip = trip,
                Seats = seatsByTrip.TryGetValue(trip.Id, out var seats) ? seats : Array.Empty<TripSeat>(),
                Stops = stopsByTrip.TryGetValue(trip.Id, out var stops) ? stops : Array.Empty<TripStop>(),
            })
            .Where(item => item.Seats.Count(seat => seat.Status == TripSeatStatus.AVAILABLE) >= request.PassengerCount)
            .Select(item => new
            {
                item.Trip,
                item.Seats,
                item.Stops,
                Points = BuildEligiblePoints(
                    item.Trip,
                    routes[item.Trip.RouteId],
                    stationsById,
                    item.Stops,
                    canonicalStops,
                    searchFilter),
            })
            .Where(item => item.Points.PickupPoints.Count > 0 && item.Points.DropoffPoints.Count > 0)
            .ToList();

        var pageItems = filtered.Take(PageSize).ToList();
        var operatorNames = await GetOperatorNamesAsync(
            pageItems.Select(item => item.Trip.OperatorId).Distinct().ToArray(),
            cancellationToken);
        var projectedItems = new List<SearchTripItem>(pageItems.Count);
        foreach (var item in pageItems)
        {
            var adjustment = await ResolveFareAdjustmentAsync(item.Trip, cancellationToken);
            projectedItems.Add(TripProjectionMapper.ToSearchTripItem(
                item.Trip,
                routes[item.Trip.RouteId],
                operatorNames[item.Trip.OperatorId],
                stationsById[routes[item.Trip.RouteId].OriginStationId],
                stationsById[routes[item.Trip.RouteId].DestinationStationId],
                item.Seats,
                item.Stops,
                adjustment,
                item.Points.PickupPoints,
                item.Points.DropoffPoints));
        }

        return SearchTripsResult.Create(projectedItems, Page, PageSize, filtered.Count);
    }

    private async Task<FareSurchargeAdjustment> ResolveFareAdjustmentAsync(
        Domain.Entities.Trip trip,
        CancellationToken cancellationToken)
    {
        if (fareSurchargeService is null)
            return new FareSurchargeAdjustment(trip.BaseFare.Amount, 0, 0, trip.BaseFare.Amount, null, null);

        var rule = await fareSurchargeService.ResolveAsync(
            trip.OperatorId,
            trip.DepartureDateTime,
            cancellationToken);
        return fareSurchargeService.Apply(trip.BaseFare.Amount, rule);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetOperatorNamesAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var operatorId in operatorIds)
        {
            var lookup = await identityInternalClient.GetOperatorAsync(operatorId, cancellationToken);
            if (!lookup.Found || string.IsNullOrWhiteSpace(lookup.Name))
            {
                if (lookup.FailureStatusCode == 403)
                {
                    throw new ForbiddenException("FORBIDDEN", lookup.Message ?? "Identity rejected the internal operator lookup.");
                }

                throw new ValidationException(
                    lookup.Message ?? "Operator lookup failed.",
                    [new ValidationError("operatorId", lookup.Message ?? "Operator lookup failed.")]);
            }

            names[operatorId] = lookup.Name;
        }

        return names;
    }

    private static EligiblePoints BuildEligiblePoints(
        Domain.Entities.Trip trip,
        Route route,
        IReadOnlyDictionary<Guid, Station> stationsById,
        IReadOnlyCollection<TripStop> tripStops,
        IReadOnlyDictionary<Guid, Stop> canonicalStops,
        SearchFilter filter)
    {
        var originStation = stationsById[route.OriginStationId];
        var destinationStation = stationsById[route.DestinationStationId];
        var destinationOrderIndex = tripStops.Count == 0 ? 1 : tripStops.Max(stop => stop.OrderIndex) + 1;
        var pickupCandidates = new List<SearchTripPointDto>();
        var dropoffCandidates = new List<SearchTripPointDto>();

        if (!filter.IsHierarchyMode || IsActiveLocationStation(originStation, filter.OriginLocationIds))
        {
            pickupCandidates.Add(ToStationPoint(originStation, 0, trip.DepartureDateTime, true, false));
        }

        if (!filter.IsHierarchyMode || IsActiveLocationStation(destinationStation, filter.DestinationLocationIds))
        {
            dropoffCandidates.Add(ToStationPoint(
                destinationStation,
                destinationOrderIndex,
                trip.EstimatedArrivalTime,
                false,
                true));
        }

        if (filter.IsHierarchyMode)
        {
            foreach (var tripStop in tripStops.OrderBy(stop => stop.OrderIndex))
            {
                if (!canonicalStops.TryGetValue(tripStop.StopId, out var stop))
                {
                    continue;
                }

                if (tripStop.AllowPickup && IsInScope(stop.LocationId, filter.OriginLocationIds))
                {
                    pickupCandidates.Add(ToStopPoint(stop, tripStop));
                }

                if (tripStop.AllowDropoff && IsInScope(stop.LocationId, filter.DestinationLocationIds))
                {
                    dropoffCandidates.Add(ToStopPoint(stop, tripStop));
                }
            }
        }

        var pickupPoints = pickupCandidates
            .Where(pickup => dropoffCandidates.Any(dropoff => pickup.OrderIndex < dropoff.OrderIndex))
            .OrderBy(point => point.OrderIndex)
            .ThenBy(point => point.StationId ?? point.StopId)
            .ToArray();
        var dropoffPoints = dropoffCandidates
            .Where(dropoff => pickupCandidates.Any(pickup => pickup.OrderIndex < dropoff.OrderIndex))
            .OrderBy(point => point.OrderIndex)
            .ThenBy(point => point.StationId ?? point.StopId)
            .ToArray();
        return new EligiblePoints(pickupPoints, dropoffPoints);
    }

    private static bool IsActiveLocationStation(Station station, IReadOnlySet<Guid> locationIds)
        => IsInScope(station.LocationId, locationIds) && station.IsActive && station.DeletedAt is null;

    private static bool IsInScope(Guid? locationId, IReadOnlySet<Guid> locationIds)
        => locationId.HasValue && locationIds.Contains(locationId.Value);

    private static SearchTripPointDto ToStationPoint(
        Station station,
        int orderIndex,
        DateTimeOffset estimatedTime,
        bool allowPickup,
        bool allowDropoff)
        => new(
            "STATION",
            station.Id,
            null,
            station.Name,
            station.AddressStreet,
            orderIndex,
            estimatedTime,
            allowPickup,
            allowDropoff);

    private static SearchTripPointDto ToStopPoint(Stop stop, TripStop tripStop)
        => new(
            "STOP",
            null,
            stop.Id,
            stop.Name,
            stop.Address,
            tripStop.OrderIndex,
            tripStop.EstimatedArrivalTime,
            tripStop.AllowPickup,
            tripStop.AllowDropoff);

    private async Task<SearchFilter> ResolveSearchFilterAsync(
        SearchTripsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OriginStationId.HasValue && request.DestinationStationId.HasValue)
        {
            return new SearchFilter(false, new HashSet<Guid>(), new HashSet<Guid>());
        }

        if (locationRepository is null)
        {
            throw new InvalidOperationException("Location repository is required when province codes are provided.");
        }

        var originIds = await ResolveLocationScopeAsync(
            request.OriginProvinceCode!,
            request.OriginLocationCode ?? request.OriginWardCode,
            nameof(SearchTripsQuery.OriginProvinceCode),
            request.OriginLocationCode is null
                ? nameof(SearchTripsQuery.OriginWardCode)
                : nameof(SearchTripsQuery.OriginLocationCode),
            cancellationToken);
        var destinationIds = await ResolveLocationScopeAsync(
            request.DestinationProvinceCode!,
            request.DestinationLocationCode ?? request.DestinationWardCode,
            nameof(SearchTripsQuery.DestinationProvinceCode),
            request.DestinationLocationCode is null
                ? nameof(SearchTripsQuery.DestinationWardCode)
                : nameof(SearchTripsQuery.DestinationLocationCode),
            cancellationToken);

        if (stopRepository is null)
        {
            throw new InvalidOperationException("Stop repository is required when province codes are provided.");
        }

        return new SearchFilter(true, originIds, destinationIds);
    }

    private async Task<IReadOnlySet<Guid>> ResolveLocationScopeAsync(
        string provinceCode,
        string? wardCode,
        string provinceField,
        string wardField,
        CancellationToken cancellationToken)
    {
        var province = await locationRepository!.GetActiveByCodeAsync(provinceCode, cancellationToken);
        if (province is null || !Location.IsTopLevelType(province.Type) || province.ParentLocationId.HasValue)
        {
            throw new ValidationException(
                "Province location was not found or inactive.",
                [new ValidationError(provinceField, "Province location was not found, inactive, or not top-level.")]);
        }

        if (!string.IsNullOrWhiteSpace(wardCode))
        {
            var ward = await locationRepository.GetActiveByCodeAsync(wardCode, cancellationToken);
            if (ward is null || !Location.IsLeafType(ward.Type) || ward.ParentLocationId != province.Id)
            {
                throw new ValidationException(
                    "Ward location does not belong to the selected province.",
                    [new ValidationError(wardField, "Ward location was not found, inactive, or does not belong to the selected province.")]);
            }

            return new HashSet<Guid> { ward.Id };
        }

        var children = await locationRepository.ListActiveChildrenAsync(province.Id, null, cancellationToken);
        return children
            .Where(location => Location.IsLeafType(location.Type))
            .Select(location => location.Id)
            .Append(province.Id)
            .ToHashSet();
    }

    private sealed record SearchFilter(
        bool IsHierarchyMode,
        IReadOnlySet<Guid> OriginLocationIds,
        IReadOnlySet<Guid> DestinationLocationIds);
    private sealed record EligiblePoints(
        IReadOnlyList<SearchTripPointDto> PickupPoints,
        IReadOnlyList<SearchTripPointDto> DropoffPoints);
}
