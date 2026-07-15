using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed class GetTripDetailHandler : IRequestHandler<GetTripDetailQuery, TripDetailDto>
{
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly IStopRepository stopRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopFareRepository tripStopFareRepository;
    private readonly ITripStopRepository tripStopRepository;

    public GetTripDetailHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
    }

    public Task<TripDetailDto> Handle(GetTripDetailQuery request, CancellationToken cancellationToken)
    {
        var trip = tripRepository.QueryNoTracking().FirstOrDefault(trip => trip.Id == request.TripId)
            ?? throw TripNotFound();
        var route = routeRepository.QueryNoTracking().FirstOrDefault(route => route.Id == trip.RouteId)
            ?? throw TripNotFound();
        var originStation = GetStation(route.OriginStationId);
        var destinationStation = GetStation(route.DestinationStationId);
        var seats = tripSeatRepository.QueryNoTracking()
            .Where(seat => seat.TripId == trip.Id)
            .ToArray();
        var stops = tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == trip.Id)
            .ToArray();
        var stopIds = stops.Select(stop => stop.StopId).ToArray();
        var stopDetails = stopRepository.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionary(stop => stop.Id);
        var fares = tripStopFareRepository.QueryNoTracking()
            .Where(fare => fare.TripId == trip.Id)
            .ToDictionary(fare => fare.StopId, fare => fare.FareFromThisStop.Amount);

        return Task.FromResult(TripProjectionMapper.ToTripDetailDto(
            trip,
            route,
            originStation,
            destinationStation,
            seats,
            stops,
            stopDetails,
            fares) with
        { Notes = trip.Notes });
    }

    private Station GetStation(Guid stationId) =>
        stationRepository.QueryNoTracking().FirstOrDefault(station => station.Id == stationId)
            ?? throw TripNotFound();

    private static CodedNotFoundException TripNotFound() =>
        new("TRIP_NOT_FOUND", "Trip was not found.");
}
