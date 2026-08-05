using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.RouteStops;

public sealed class AddRouteStopHandler : IRequestHandler<AddRouteStopCommand, RouteStopDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public AddRouteStopHandler(
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IRouteStopRepository routeStopRepository,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<RouteStopDto> Handle(AddRouteStopCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        if (!request.AllowPickup && !request.AllowDropoff)
        {
            throw new CodedValidationException(
                "ROUTE_STOP_FLAGS_INVALID",
                "Route stop must allow pickup or dropoff.",
                [new ValidationError("allowPickup", "Route stop must allow pickup or dropoff.")]);
        }

        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        await ValidateStopBelongsToOperatorAsync(request.OperatorId, request.StopId, cancellationToken);
        await ValidateRouteStopDoesNotExistAsync(request.RouteId, request.StopId, cancellationToken);
        await ValidateOrderIndexAvailableAsync(request.RouteId, request.OrderIndex, cancellationToken);

        var routeStop = RouteStop.Create(
            request.RouteId,
            request.StopId,
            request.OrderIndex,
            request.EstimatedDurationFromOriginMinutes,
            request.DistanceFromOriginKm,
            request.AllowPickup,
            request.AllowDropoff);

        await routeStopRepository.AddAsync(routeStop, cancellationToken);
        route.SetPathGeometry(null);
        routeRepository.Update(route);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RouteStopMapper.ToDto(routeStop);
    }

    private async Task ValidateStopBelongsToOperatorAsync(Guid operatorId, Guid stopId, CancellationToken cancellationToken)
    {
        var stop = await stopRepository.GetByIdAsync(stopId, cancellationToken);
        if (stop is null || stop.OperatorId != operatorId || !stop.IsActive || stop.DeletedAt is not null)
        {
            throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        }
    }

    private async Task ValidateRouteStopDoesNotExistAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken)
    {
        if (await routeStopRepository.GetByRouteAndStopAsync(routeId, stopId, cancellationToken) is not null)
        {
            throw new CodedValidationException(
                "ROUTE_STOP_DUPLICATED",
                "Stop is already configured on this route.",
                [new ValidationError("stopId", "Stop is already configured on this route.")]);
        }
    }

    private async Task ValidateOrderIndexAvailableAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken)
    {
        if (await routeStopRepository.ExistsByRouteAndOrderIndexAsync(routeId, orderIndex, cancellationToken))
        {
            throw new CodedValidationException(
                "ROUTE_STOP_ORDER_INVALID",
                "Route stop order index is already used on this route.",
                [new ValidationError("orderIndex", "Order index is already used on this route.")]);
        }
    }
}
