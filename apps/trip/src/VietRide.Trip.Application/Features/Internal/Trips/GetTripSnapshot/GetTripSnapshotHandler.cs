using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;

public sealed class GetTripSnapshotHandler : IRequestHandler<GetTripSnapshotQuery, InternalTripSnapshotDto>
{
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopFareTemplateRepository routeStopFareTemplateRepository;
    private readonly IStationRepository stationRepository;
    private readonly IStopRepository stopRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopFareRepository tripStopFareRepository;
    private readonly ITripStopRepository tripStopRepository;

    public GetTripSnapshotHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IRouteStopFareTemplateRepository routeStopFareTemplateRepository,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.routeStopFareTemplateRepository = routeStopFareTemplateRepository;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
    }

    public async Task<InternalTripSnapshotDto> Handle(GetTripSnapshotQuery request, CancellationToken cancellationToken)
    {
        var trip = tripRepository.QueryNoTracking().FirstOrDefault(trip => trip.Id == request.TripId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var route = routeRepository.QueryNoTracking().FirstOrDefault(route => route.Id == trip.RouteId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip route snapshot was not found.");
        var originStation = GetStation(route.OriginStationId);
        var destinationStation = GetStation(route.DestinationStationId);
        var fares = await ResolveFaresAsync(trip, request.PricingAt, cancellationToken);
        var tripStops = tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == trip.Id)
            .OrderBy(stop => stop.OrderIndex)
            .ToArray();
        var stopIds = tripStops.Select(stop => stop.StopId).ToArray();
        var activeStops = stopRepository.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionary(stop => stop.Id, stop => stop.IsActive && stop.DeletedAt == null);
        var stops = tripStops
            .Select(stop => new InternalTripStopSnapshotDto(
                stop.StopId,
                stop.OrderIndex,
                stop.AllowPickup,
                stop.AllowDropoff,
                stop.EstimatedArrivalTime,
                stop.DistanceFromOriginKm.HasValue ? (double)stop.DistanceFromOriginKm.Value : null,
                fares.TryGetValue(stop.StopId, out var fare)
                    ? fare
                    : request.PricingAt.HasValue ? trip.BaseFare.Amount : null,
                stop.Status.ToString(),
                stop.ActualArrivalTime,
                activeStops.GetValueOrDefault(stop.StopId)))
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
            new InternalTripStationSnapshotDto(
                originStation.Id,
                originStation.Name,
                originStation.SupportsShuttle,
                originStation.Latitude,
                originStation.Longitude,
                originStation.IsActive),
            new InternalTripStationSnapshotDto(
                destinationStation.Id,
                destinationStation.Name,
                destinationStation.SupportsShuttle,
                destinationStation.Latitude,
                destinationStation.Longitude,
                destinationStation.IsActive),
            stops,
            new InternalTripSeatSummaryDto(seats.Length, seats.Count(seat => seat.Status == TripSeatStatus.AVAILABLE)),
            route.ReturnRouteId,
            trip.DriverUserId,
            trip.AssistantUserId,
            trip.DestinationArrivedAt,
            trip.ActualDepartureTime);

        return dto;
    }

    private async Task<IReadOnlyDictionary<Guid, long>> ResolveFaresAsync(
        VietRide.Trip.Domain.Entities.Trip trip,
        DateTimeOffset? pricingAt,
        CancellationToken cancellationToken)
    {
        if (!pricingAt.HasValue)
        {
            var persistedFares = await tripStopFareRepository.ListByTripAsync(trip.Id, null, cancellationToken);
            return persistedFares.ToDictionary(fare => fare.StopId, fare => fare.FareFromThisStop.Amount);
        }

        var suppliedInstant = pricingAt.Value.ToUniversalTime();
        var manualOverrides = await tripStopFareRepository.ListByTripAsync(
            trip.Id,
            TripStopFareSource.MANUAL_OVERRIDE,
            cancellationToken);
        var activeTemplates = await routeStopFareTemplateRepository.ListActiveByRouteAsync(
            trip.RouteId,
            suppliedInstant,
            cancellationToken);

        var resolved = activeTemplates.ToDictionary(
            template => template.StopId,
            template => template.FareFromThisStop.Amount);
        foreach (var manualOverride in manualOverrides)
        {
            resolved[manualOverride.StopId] = manualOverride.FareFromThisStop.Amount;
        }

        return resolved;
    }

    private Station GetStation(Guid stationId) =>
        stationRepository.QueryNoTracking().FirstOrDefault(station => station.Id == stationId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip station snapshot was not found.");
}
