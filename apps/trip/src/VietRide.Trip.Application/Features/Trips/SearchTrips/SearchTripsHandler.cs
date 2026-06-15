using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed class SearchTripsHandler : IRequestHandler<SearchTripsQuery, SearchTripsResult>
{
    private const int Page = 1;
    private const int PageSize = 20;

    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopRepository tripStopRepository;

    public SearchTripsHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        IIdentityInternalClient identityInternalClient)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.identityInternalClient = identityInternalClient;
    }

    public async Task<SearchTripsResult> Handle(SearchTripsQuery request, CancellationToken cancellationToken)
    {
        var localStart = new DateTimeOffset(request.DepartureDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        var start = localStart.ToUniversalTime();
        var end = localStart.AddDays(1).ToUniversalTime();
        var routes = routeRepository.QueryNoTracking()
            .Where(route => route.OriginStationId == request.OriginStationId
                && route.DestinationStationId == request.DestinationStationId
                && route.DeletedAt == null
                && route.IsActive)
            .ToDictionary(route => route.Id);

        if (routes.Count == 0)
        {
            return SearchTripsResult.Create([], Page, PageSize, 0);
        }

        var originStation = GetStation(request.OriginStationId);
        var destinationStation = GetStation(request.DestinationStationId);
        if (originStation is null || destinationStation is null)
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
        var items = pageItems
            .Select(item => TripProjectionMapper.ToSearchTripItem(
                item.Trip,
                routes[item.Trip.RouteId],
                operatorNames[item.Trip.OperatorId],
                originStation,
                destinationStation,
                item.Seats,
                item.Stops))
            .ToList();

        return SearchTripsResult.Create(items, Page, PageSize, filtered.Count);
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

    private Station? GetStation(Guid stationId) =>
        stationRepository.QueryNoTracking().FirstOrDefault(station => station.Id == stationId);
}
