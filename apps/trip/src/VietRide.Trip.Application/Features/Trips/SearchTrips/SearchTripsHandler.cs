using MediatR;
using VietRide.Shared.Application.Exceptions;
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
    private readonly IFareSurchargeService? fareSurchargeService;

    public SearchTripsHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ILocationRepository locationRepository,
        IIdentityInternalClient identityInternalClient,
        IFareSurchargeService? fareSurchargeService = null)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.locationRepository = locationRepository;
        this.identityInternalClient = identityInternalClient;
        this.fareSurchargeService = fareSurchargeService;
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
        var localStart = new DateTimeOffset(request.DepartureDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        var start = localStart.ToUniversalTime();
        var end = localStart.AddDays(1).ToUniversalTime();
        var stationFilter = await ResolveStationFilterAsync(request, cancellationToken);
        if (stationFilter.OriginStationIds.Count == 0 || stationFilter.DestinationStationIds.Count == 0)
        {
            return SearchTripsResult.Create([], Page, PageSize, 0);
        }

        var routes = routeRepository.QueryNoTracking()
            .Where(route => stationFilter.OriginStationIds.Contains(route.OriginStationId)
                && stationFilter.DestinationStationIds.Contains(route.DestinationStationId)
                && route.DeletedAt == null
                && route.IsActive)
            .ToDictionary(route => route.Id);

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
                && stationsById.ContainsKey(route.DestinationStationId))
            .ToDictionary(route => route.Id);
        if (routes.Count == 0)
        {
            return SearchTripsResult.Create([], Page, PageSize, 0);
        }

        var routeIds = routes.Keys.ToHashSet();
        var candidates = tripRepository.QueryNoTracking()
            .Where(trip => routeIds.Contains(trip.RouteId)
                && (trip.Status == TripStatus.SCHEDULED || trip.Status == TripStatus.BOARDING)
                && trip.DepartureDateTime >= start
                && trip.DepartureDateTime < end)
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

        var filtered = candidates
            .Select(trip => new
            {
                Trip = trip,
                Seats = seatsByTrip.TryGetValue(trip.Id, out var seats) ? seats : Array.Empty<TripSeat>(),
                Stops = stopsByTrip.TryGetValue(trip.Id, out var stops) ? stops : Array.Empty<TripStop>(),
            })
            .Where(item => item.Seats.Count(seat => seat.Status == TripSeatStatus.AVAILABLE) >= request.PassengerCount)
            .Where(item => request.AllowAlongRoutePickup != true || item.Stops.Any(stop => stop.AllowPickup))
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
                adjustment));
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

    private async Task<StationFilter> ResolveStationFilterAsync(
        SearchTripsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OriginStationId.HasValue && request.DestinationStationId.HasValue)
        {
            return new StationFilter(
                new HashSet<Guid> { request.OriginStationId.Value },
                new HashSet<Guid> { request.DestinationStationId.Value });
        }

        if (locationRepository is null)
        {
            throw new InvalidOperationException("Location repository is required when location codes are provided.");
        }

        var originLocation = await locationRepository.GetActiveByCodeAsync(request.OriginLocationCode!, cancellationToken);
        if (originLocation is null)
        {
            throw new ValidationException(
                "Origin location was not found or inactive.",
                [new ValidationError(nameof(SearchTripsQuery.OriginLocationCode), "Origin location was not found or inactive.")]);
        }

        var destinationLocation = await locationRepository.GetActiveByCodeAsync(request.DestinationLocationCode!, cancellationToken);
        if (destinationLocation is null)
        {
            throw new ValidationException(
                "Destination location was not found or inactive.",
                [new ValidationError(nameof(SearchTripsQuery.DestinationLocationCode), "Destination location was not found or inactive.")]);
        }

        var originStationIds = stationRepository.QueryNoTracking()
            .Where(station => station.LocationId == originLocation.Id && station.IsActive && station.DeletedAt == null)
            .Select(station => station.Id)
            .ToHashSet();
        var destinationStationIds = stationRepository.QueryNoTracking()
            .Where(station => station.LocationId == destinationLocation.Id && station.IsActive && station.DeletedAt == null)
            .Select(station => station.Id)
            .ToHashSet();

        return new StationFilter(originStationIds, destinationStationIds);
    }

    private sealed record StationFilter(
        IReadOnlySet<Guid> OriginStationIds,
        IReadOnlySet<Guid> DestinationStationIds);
}
