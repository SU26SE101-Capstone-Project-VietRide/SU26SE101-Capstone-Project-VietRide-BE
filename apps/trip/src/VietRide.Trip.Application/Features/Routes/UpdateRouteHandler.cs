using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class UpdateRouteHandler : IRequestHandler<UpdateRouteCommand, RouteDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly IStationRepository? stationRepository;
    private readonly IRouteStopRepository? routeStopRepository;
    private readonly IStopRepository? stopRepository;

    public UpdateRouteHandler(
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IUnitOfWork unitOfWork,
        IStationRepository? stationRepository = null,
        IRouteStopRepository? routeStopRepository = null,
        IStopRepository? stopRepository = null)
    {
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.unitOfWork = unitOfWork;
        this.stationRepository = stationRepository;
        this.routeStopRepository = routeStopRepository;
        this.stopRepository = stopRepository;
    }

    public async Task<RouteDto> Handle(UpdateRouteCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        await ValidateReturnRouteAsync(request.OperatorId, request.ReturnRouteId, cancellationToken);

        var returnRouteId = request.HasReturnRouteId ? request.ReturnRouteId : route.ReturnRouteId;
        var effectiveName = request.Name ?? route.Name;
        var duplicate = await routeRepository.FindDuplicateWithTransactionLockAsync(
            request.OperatorId,
            effectiveName,
            route.OriginStationId,
            route.DestinationStationId,
            route.Id,
            cancellationToken);
        if (duplicate is not null)
        {
            throw new CodedConflictException(
                "ROUTE_DUPLICATED",
                "A Route with the same normalized name and station pair already exists.",
                [new ValidationError("existingRouteId", duplicate.Id.ToString("D"))]);
        }

        route.UpdateDetails(
            effectiveName,
            route.OriginStationId,
            route.DestinationStationId,
            Money.FromRaw(request.BaseFare ?? route.BaseFare.Amount),
            request.TotalDistanceKm ?? route.TotalDistanceKm,
            request.EstimatedDurationMinutes ?? route.EstimatedDurationMinutes,
            returnRouteId);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
            {
                route.Activate();
            }
            else
            {
                route.Deactivate();
            }
        }

        routeRepository.Update(route);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RouteDetailsProjector.Project(route, stationRepository, routeStopRepository, stopRepository);
    }

    private async Task ValidateReturnRouteAsync(Guid operatorId, Guid? returnRouteId, CancellationToken cancellationToken)
    {
        if (!returnRouteId.HasValue)
        {
            return;
        }

        if (!await routeRepository.ExistsActiveOwnedByOperatorAsync(operatorId, returnRouteId.Value, cancellationToken))
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Return route was not found.");
        }
    }
}
