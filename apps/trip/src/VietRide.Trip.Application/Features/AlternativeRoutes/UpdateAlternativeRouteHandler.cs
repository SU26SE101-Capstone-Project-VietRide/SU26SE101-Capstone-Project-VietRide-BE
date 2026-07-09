using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class UpdateAlternativeRouteHandler : IRequestHandler<UpdateAlternativeRouteCommand, AlternativeRouteDto>
{
    private const int MaxActiveAlternativeRoutesPerRoute = 2;

    private readonly IAlternativeRouteRepository alternativeRouteRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IStationRepository stationRepository;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public UpdateAlternativeRouteHandler(
        IAlternativeRouteRepository alternativeRouteRepository,
        IIdentityInternalClient identityInternalClient,
        IStationRepository stationRepository,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.alternativeRouteRepository = alternativeRouteRepository;
        this.identityInternalClient = identityInternalClient;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<AlternativeRouteDto> Handle(UpdateAlternativeRouteCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(identityInternalClient, request.OperatorId, cancellationToken);

        var alternativeRoute = await alternativeRouteRepository.GetOwnedByIdAsync(
            request.OperatorId,
            request.AlternativeRouteId,
            cancellationToken);
        if (alternativeRoute is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
        }

        if (!alternativeRoute.IsActive && request.IsActive == true)
        {
            await ValidateActiveLimitAsync(alternativeRoute.RouteId, cancellationToken);
        }

        if (request.HasDestinationStationId && request.DestinationStationId.HasValue)
        {
            await ValidateStationExistsAsync(request.DestinationStationId.Value, cancellationToken);
        }

        if (request.HasStops)
        {
            await ValidateStopsAsync(request.OperatorId, request.Stops!, cancellationToken);
        }

        var geometryChanged = request.HasStops
            || (request.HasDestinationStationId
                && request.DestinationStationId != alternativeRoute.DestinationStationId);

        alternativeRoute.UpdateDetails(
            request.HasName ? request.Name! : alternativeRoute.Name,
            request.HasDestinationStationId ? request.DestinationStationId!.Value : alternativeRoute.DestinationStationId,
            request.HasTotalDistanceKm ? request.TotalDistanceKm : alternativeRoute.TotalDistanceKm,
            request.HasEstimatedDurationMinutes ? request.EstimatedDurationMinutes : alternativeRoute.EstimatedDurationMinutes,
            request.HasDescription ? request.Description : alternativeRoute.Description);

        if (geometryChanged)
        {
            alternativeRoute.SetPathGeometry(null);
        }

        if (request.IsActive == true)
        {
            alternativeRoute.Activate();
        }
        else if (request.IsActive == false)
        {
            alternativeRoute.Deactivate();
        }

        IReadOnlyList<AlternativeRouteStop> stops;
        if (request.HasStops)
        {
            stops = request.Stops!
                .Select(stop => AlternativeRouteStop.Create(
                    alternativeRoute.Id,
                    stop.StopId,
                    stop.OrderIndex,
                    stop.EstimatedDurationFromOriginMinutes,
                    stop.DistanceFromOriginKm))
                .ToList();
            await alternativeRouteRepository.ReplaceStopsAsync(alternativeRoute.Id, stops, cancellationToken);
        }
        else
        {
            stops = await alternativeRouteRepository.ListStopsAsync(alternativeRoute.Id, cancellationToken);
        }

        alternativeRouteRepository.Update(alternativeRoute);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AlternativeRouteMapper.ToDto(alternativeRoute, stops);
    }

    private async Task ValidateActiveLimitAsync(Guid routeId, CancellationToken cancellationToken)
    {
        var activeCount = await alternativeRouteRepository.CountActiveByRouteAsync(routeId, cancellationToken);
        if (activeCount >= MaxActiveAlternativeRoutesPerRoute)
        {
            throw new CodedValidationException(
                "ALTERNATIVE_ROUTE_LIMIT_EXCEEDED",
                "A route can have at most two active alternative routes.",
                [new ValidationError("alternativeRoutes", "A route can have at most two active alternative routes.")]);
        }
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
