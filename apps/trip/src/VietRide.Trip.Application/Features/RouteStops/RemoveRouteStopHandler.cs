using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.RouteStops;

public sealed class RemoveRouteStopHandler : IRequestHandler<RemoveRouteStopCommand, Unit>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly ITripStopSnapshotSyncService snapshotSync;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public RemoveRouteStopHandler(
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IRouteStopRepository routeStopRepository,
        ITripStopSnapshotSyncService snapshotSync,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.snapshotSync = snapshotSync;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
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

        var targetStops = routeStopRepository.QueryNoTracking()
            .Where(item => item.RouteId == route.Id && item.StopId != routeStop.StopId)
            .OrderBy(item => item.OrderIndex)
            .ToArray();
        var now = clock.UtcNow;
        var preflight = await snapshotSync.PreflightAsync(
            route.Id,
            request.OperatorId,
            now,
            cancellationToken);

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            routeStopRepository.Remove(routeStop);
            route.SetPathGeometry(null);
            routeRepository.Update(route);
            await snapshotSync.SynchronizeAsync(
                preflight,
                targetStops,
                request.ActorUserId,
                "REMOVE_STOP",
                now,
                cancellationToken);
            return Unit.Value;
        }, cancellationToken);
    }
}
