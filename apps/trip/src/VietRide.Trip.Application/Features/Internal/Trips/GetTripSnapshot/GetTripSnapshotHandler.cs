using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
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
    private readonly IFareSurchargeService? fareSurchargeService;

    public GetTripSnapshotHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IRouteStopFareTemplateRepository routeStopFareTemplateRepository,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository,
        IFareSurchargeService? fareSurchargeService = null)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.routeStopFareTemplateRepository = routeStopFareTemplateRepository;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
        this.fareSurchargeService = fareSurchargeService;
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
        var surchargeRule = request.PricingAt.HasValue && fareSurchargeService is not null
            ? await fareSurchargeService.ResolveAsync(trip.OperatorId, trip.DepartureDateTime, cancellationToken)
            : null;
        var baseFareAdjustment = ApplySurcharge(trip.BaseFare.Amount, surchargeRule);
        var tripStops = tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == trip.Id)
            .OrderBy(stop => stop.OrderIndex)
            .ToArray();
        var stopIds = tripStops.Select(stop => stop.StopId).ToArray();
        var stopsById = stopRepository.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionary(stop => stop.Id);
        var stops = tripStops
            .Select(stop =>
            {
                stopsById.TryGetValue(stop.StopId, out var stopDefinition);
                var originalFare = fares.TryGetValue(stop.StopId, out var fare)
                    ? fare
                    : request.PricingAt.HasValue ? trip.BaseFare.Amount : (long?)null;
                var adjustment = originalFare.HasValue
                    ? ApplySurcharge(originalFare.Value, surchargeRule)
                    : null;
                var snapshot = new InternalTripStopSnapshotDto(
                stop.StopId,
                stop.OrderIndex,
                stop.AllowPickup,
                stop.AllowDropoff,
                stop.EstimatedArrivalTime,
                stop.DistanceFromOriginKm.HasValue ? (double)stop.DistanceFromOriginKm.Value : null,
                adjustment?.EffectiveFare,
                stop.Status.ToString(),
                stop.ActualArrivalTime,
                stopDefinition is not null && stopDefinition.IsActive && stopDefinition.DeletedAt == null,
                stopDefinition?.Name);

                return request.PricingAt.HasValue
                    ? snapshot with
                    {
                        OriginalFareFromThisStop = originalFare,
                        SurchargePercent = adjustment?.SurchargePercent ?? 0,
                        SurchargeAmount = adjustment?.SurchargeAmount ?? 0,
                    }
                    : snapshot;
            })
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
            baseFareAdjustment.EffectiveFare,
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
            trip.ActualDepartureTime,
            route.TotalDistanceKm.HasValue ? (double)route.TotalDistanceKm.Value : null);

        return request.PricingAt.HasValue
            ? dto with
            {
                OriginalBaseFare = trip.BaseFare.Amount,
                SurchargePercent = baseFareAdjustment.SurchargePercent,
                SurchargeAmount = baseFareAdjustment.SurchargeAmount,
                SurchargePeriodId = baseFareAdjustment.SurchargePeriodId,
                SurchargePeriodName = baseFareAdjustment.SurchargePeriodName,
            }
            : dto;
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

    private FareSurchargeAdjustment ApplySurcharge(long fare, FareSurchargeRule? rule)
        => fareSurchargeService?.Apply(fare, rule)
            ?? new FareSurchargeAdjustment(fare, 0, 0, fare, null, null);

    private Station GetStation(Guid stationId) =>
        stationRepository.QueryNoTracking().FirstOrDefault(station => station.Id == stationId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip station snapshot was not found.");
}
