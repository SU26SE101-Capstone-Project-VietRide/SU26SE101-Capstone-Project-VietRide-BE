using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;

public sealed class GetTripSnapshotHandler : IRequestHandler<GetTripSnapshotQuery, InternalTripSnapshotDto>
{
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopFareRepository tripStopFareRepository;
    private readonly ITripStopRepository tripStopRepository;

    public GetTripSnapshotHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
    }

    public Task<InternalTripSnapshotDto> Handle(GetTripSnapshotQuery request, CancellationToken cancellationToken)
    {
        var trip = tripRepository.QueryNoTracking().FirstOrDefault(trip => trip.Id == request.TripId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var route = routeRepository.QueryNoTracking().FirstOrDefault(route => route.Id == trip.RouteId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip route snapshot was not found.");
        var originStation = GetStation(route.OriginStationId);
        var destinationStation = GetStation(route.DestinationStationId);
        var fares = tripStopFareRepository.QueryNoTracking()
            .Where(fare => fare.TripId == trip.Id)
            .ToDictionary(fare => fare.StopId, fare => fare.FareFromThisStop.Amount);
        var stops = tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == trip.Id)
            .OrderBy(stop => stop.OrderIndex)
            .ToArray()
            .Select(stop => new InternalTripStopSnapshotDto(
                stop.StopId,
                stop.OrderIndex,
                stop.AllowPickup,
                stop.AllowDropoff,
                stop.EstimatedArrivalTime,
                stop.DistanceFromOriginKm.HasValue ? (double)stop.DistanceFromOriginKm.Value : null,
                fares.TryGetValue(stop.StopId, out var fare) ? fare : null,
                stop.Status.ToString(),
                stop.ActualArrivalTime))
            .ToArray();
        var seats = tripSeatRepository.QueryNoTracking().Where(seat => seat.TripId == trip.Id).ToArray();

        var dto = new InternalTripSnapshotDto(
            trip.Id,
            trip.OperatorId,
            trip.RouteId,
            trip.VehicleId,
            trip.Status.ToString(),
            trip.DepartureDateTime,
            trip.EstimatedArrivalTime,
            trip.BaseFare.Amount,
            new InternalTripStationSnapshotDto(originStation.Id, originStation.Name),
            new InternalTripStationSnapshotDto(destinationStation.Id, destinationStation.Name),
            stops,
            new InternalTripSeatSummaryDto(seats.Length, seats.Count(seat => seat.Status == TripSeatStatus.AVAILABLE)),
            route.ReturnRouteId);

        return Task.FromResult(dto);
    }

    private Station GetStation(Guid stationId) =>
        stationRepository.QueryNoTracking().FirstOrDefault(station => station.Id == stationId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip station snapshot was not found.");
}
