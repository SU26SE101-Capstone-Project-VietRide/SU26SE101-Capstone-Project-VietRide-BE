using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
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
    private readonly IFareSurchargeService? fareSurchargeService;
    private readonly IRouteStopFareTemplateRepository? routeStopFareTemplateRepository;
    private readonly IClock? clock;

    public GetTripDetailHandler(
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository,
        IFareSurchargeService? fareSurchargeService = null,
        IRouteStopFareTemplateRepository? routeStopFareTemplateRepository = null,
        IClock? clock = null)
    {
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
        this.fareSurchargeService = fareSurchargeService;
        this.routeStopFareTemplateRepository = routeStopFareTemplateRepository;
        this.clock = clock;
    }

    public async Task<TripDetailDto> Handle(GetTripDetailQuery request, CancellationToken cancellationToken)
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
        var fares = await ResolveFaresAsync(trip, cancellationToken);

        var rule = fareSurchargeService is null
            ? null
            : await fareSurchargeService.ResolveAsync(trip.OperatorId, trip.DepartureDateTime, cancellationToken);
        var baseFareAdjustment = ApplySurcharge(trip.BaseFare.Amount, rule);
        var fareAdjustments = fares.ToDictionary(
            fare => fare.Key,
            fare => ApplySurcharge(fare.Value, rule));

        return TripProjectionMapper.ToTripDetailDto(
            trip,
            route,
            originStation,
            destinationStation,
            seats,
            stops,
            stopDetails,
            fares,
            baseFareAdjustment,
            fareAdjustments) with
        { Notes = trip.Notes };
    }

    private FareSurchargeAdjustment ApplySurcharge(long fare, FareSurchargeRule? rule)
        => fareSurchargeService?.Apply(fare, rule)
            ?? new FareSurchargeAdjustment(fare, 0, 0, fare, null, null);

    private async Task<IReadOnlyDictionary<Guid, long>> ResolveFaresAsync(
        VietRide.Trip.Domain.Entities.Trip trip,
        CancellationToken cancellationToken)
    {
        var persistedFares = tripStopFareRepository.QueryNoTracking()
            .Where(fare => fare.TripId == trip.Id)
            .ToArray();
        if (routeStopFareTemplateRepository is null || clock is null)
        {
            return persistedFares.ToDictionary(
                fare => fare.StopId,
                fare => fare.FareFromThisStop.Amount);
        }

        var activeTemplates = await routeStopFareTemplateRepository.ListActiveByRouteAsync(
            trip.RouteId,
            clock.UtcNow,
            cancellationToken);
        var resolved = activeTemplates.ToDictionary(
            template => template.StopId,
            template => template.FareFromThisStop.Amount);
        foreach (var manualOverride in persistedFares.Where(
                     fare => fare.Source == TripStopFareSource.MANUAL_OVERRIDE))
        {
            resolved[manualOverride.StopId] = manualOverride.FareFromThisStop.Amount;
        }

        return resolved;
    }

    private Station GetStation(Guid stationId) =>
        stationRepository.QueryNoTracking().FirstOrDefault(station => station.Id == stationId)
            ?? throw TripNotFound();

    private static CodedNotFoundException TripNotFound() =>
        new("TRIP_NOT_FOUND", "Trip was not found.");
}
