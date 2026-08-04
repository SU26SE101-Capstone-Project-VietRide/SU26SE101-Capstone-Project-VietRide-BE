using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class CreateAlternativeRouteHandler : IRequestHandler<CreateAlternativeRouteCommand, AlternativeRouteDto>
{
    private readonly IAlternativeRouteRepository alternativeRouteRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateAlternativeRouteHandler(
        IAlternativeRouteRepository alternativeRouteRepository,
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.alternativeRouteRepository = alternativeRouteRepository;
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<AlternativeRouteDto> Handle(CreateAlternativeRouteCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(identityInternalClient, request.OperatorId, cancellationToken);

        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        await ValidateStationExistsAsync(request.DestinationStationId, cancellationToken);
        await ValidateStopsAsync(request.OperatorId, request.Stops, cancellationToken);

        var alternativeRoute = AlternativeRoute.Create(
            request.RouteId,
            request.Name!,
            request.DestinationStationId,
            request.TotalDistanceKm,
            request.EstimatedDurationMinutes,
            request.Description);
        var stops = request.Stops
            .Select(stop => AlternativeRouteStop.Create(
                alternativeRoute.Id,
                stop.StopId,
                stop.OrderIndex,
                stop.EstimatedDurationFromOriginMinutes,
                stop.DistanceFromOriginKm))
            .ToList();

        await alternativeRouteRepository.AddAsync(alternativeRoute, cancellationToken);
        await alternativeRouteRepository.ReplaceStopsAsync(alternativeRoute.Id, stops, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AlternativeRouteMapper.ToDto(alternativeRoute, stops);
    }

    private async Task ValidateStationExistsAsync(Guid stationId, CancellationToken cancellationToken)
    {
        var station = await stationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station is null || !station.IsActive || station.DeletedAt is not null)
        {
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        }
    }

    private async Task ValidateStopsAsync(Guid operatorId, IReadOnlyList<AlternativeRouteStopInput> stops, CancellationToken cancellationToken)
    {
        ValidateDuplicateStops(stops);
        ValidateDuplicateOrderIndexes(stops);

        foreach (var stopInput in stops)
        {
            var stop = await stopRepository.GetByIdAsync(stopInput.StopId, cancellationToken);
            if (stop is null || stop.OperatorId != operatorId || !stop.IsActive || stop.DeletedAt is not null)
            {
                throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
            }
        }
    }

    private static void ValidateDuplicateStops(IReadOnlyList<AlternativeRouteStopInput> stops)
    {
        var duplicateStopIds = stops.GroupBy(stop => stop.StopId).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicateStopIds.Count > 0)
        {
            throw new ValidationException(
                "Stop is already configured on this alternative route.",
                [new ValidationError("stopId", "Stop is already configured on this alternative route.")]);
        }
    }

    private static void ValidateDuplicateOrderIndexes(IReadOnlyList<AlternativeRouteStopInput> stops)
    {
        var duplicateOrderIndexes = stops.GroupBy(stop => stop.OrderIndex).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicateOrderIndexes.Count > 0)
        {
            throw new ValidationException(
                "Alternative route stop order index is already used on this alternative route.",
                [new ValidationError("orderIndex", "Order index is already used on this alternative route.")]);
        }
    }
}
