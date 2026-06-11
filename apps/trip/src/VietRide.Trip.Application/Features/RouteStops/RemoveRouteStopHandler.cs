using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.RouteStops;

public sealed class RemoveRouteStopHandler : IRequestHandler<RemoveRouteStopCommand, Unit>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly IUnitOfWork unitOfWork;

    public RemoveRouteStopHandler(
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IRouteStopRepository routeStopRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(RemoveRouteStopCommand request, CancellationToken cancellationToken)
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

        var routeStop = await routeStopRepository.GetByRouteAndStopAsync(request.RouteId, request.StopId, cancellationToken);
        if (routeStop is null)
        {
            throw new CodedNotFoundException("STOP_NOT_FOUND", "Route stop was not found.");
        }

        routeStopRepository.Remove(routeStop);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
