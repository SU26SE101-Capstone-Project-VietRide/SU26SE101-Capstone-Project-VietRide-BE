using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class CreateRouteHandler : IRequestHandler<CreateRouteCommand, RouteDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IOperatorStationRepository operatorStationRepository;
    private readonly IRouteRepository routeRepository;
    private readonly IStationRepository stationRepository;
    private readonly IRouteStopRepository? routeStopRepository;
    private readonly IStopRepository? stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateRouteHandler(
        IIdentityInternalClient identityInternalClient,
        IOperatorStationRepository operatorStationRepository,
        IRouteRepository routeRepository,
        IStationRepository stationRepository,
        IUnitOfWork unitOfWork,
        IRouteStopRepository? routeStopRepository = null,
        IStopRepository? stopRepository = null)
    {
        this.identityInternalClient = identityInternalClient;
        this.operatorStationRepository = operatorStationRepository;
        this.routeRepository = routeRepository;
        this.stationRepository = stationRepository;
        this.unitOfWork = unitOfWork;
        this.routeStopRepository = routeStopRepository;
        this.stopRepository = stopRepository;
    }

    public async Task<RouteDto> Handle(CreateRouteCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);
        await StopWriteEligibilityGuard.ValidateOperatorSubscriptionCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            requireShuttleModule: false,
            cancellationToken);

        await ValidateStationExistsAsync(request.OriginStationId, cancellationToken);
        await ValidateStationExistsAsync(request.DestinationStationId, cancellationToken);
        ValidateOperatorStationLinks(request.OperatorId, request.OriginStationId, request.DestinationStationId);
        ValidateDifferentStations(request.OriginStationId, request.DestinationStationId);
        await ValidateReturnRouteAsync(request.OperatorId, request.ReturnRouteId, cancellationToken);
        await EnsureNotDuplicatedAsync(
            request.OperatorId,
            request.Name!,
            request.OriginStationId,
            request.DestinationStationId,
            null,
            cancellationToken);

        var route = Route.Create(
            request.OperatorId,
            request.Name!,
            request.OriginStationId,
            request.DestinationStationId,
            Money.FromRaw(request.BaseFare),
            request.TotalDistanceKm,
            request.EstimatedDurationMinutes,
            request.ReturnRouteId);

        if (request.IsActive == false)
        {
            route.Deactivate();
        }

        var quotaClient = identityInternalClient as ISubscriptionQuotaClient;
        var quota = quotaClient is null ? null : await quotaClient.ClaimQuotaAllocationAsync(
            request.OperatorId,
            "ROUTES",
            route.Id,
            periodKey: null,
            cancellationToken);
        if (quota is not null && !quota.IsAllowed)
            throw new CodedValidationException(quota.ErrorCode ?? "SUBSCRIPTION_LIMIT_EXCEEDED", quota.Message ?? "Subscription route limit exceeded.");

        try
        {
            await routeRepository.AddAsync(route, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (quota?.AllocationId.HasValue == true && quota.AllocationId.Value != Guid.Empty)
                await quotaClient!.ReleaseQuotaAllocationAsync(request.OperatorId, quota.AllocationId.Value, cancellationToken);
            throw;
        }

        return RouteDetailsProjector.Project(route, stationRepository, routeStopRepository, stopRepository);
    }

    private async Task ValidateStationExistsAsync(Guid stationId, CancellationToken cancellationToken)
    {
        var station = await stationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station is null || !station.IsActive || station.DeletedAt is not null)
        {
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        }
    }

    private void ValidateOperatorStationLinks(Guid operatorId, Guid originStationId, Guid destinationStationId)
    {
        var linkedStationIds = operatorStationRepository.QueryNoTracking()
            .Where(link =>
                link.OperatorId == operatorId
                && link.IsActive
                && (link.StationId == originStationId || link.StationId == destinationStationId))
            .Select(link => link.StationId)
            .ToHashSet();

        var errors = new List<ValidationError>();
        if (!linkedStationIds.Contains(originStationId))
        {
            errors.Add(new ValidationError(
                nameof(CreateRouteCommand.OriginStationId),
                "Operator has no active link to operate the origin station."));
        }

        if (!linkedStationIds.Contains(destinationStationId))
        {
            errors.Add(new ValidationError(
                nameof(CreateRouteCommand.DestinationStationId),
                "Operator has no active link to operate the destination station."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Operator has no active link to operate one or more route stations.", errors);
        }
    }

    private static void ValidateDifferentStations(Guid originStationId, Guid destinationStationId)
    {
        if (originStationId == destinationStationId)
        {
            throw new ValidationException(
                "Origin and destination stations must be different.",
                [new ValidationError(nameof(CreateRouteCommand.DestinationStationId), "Destination station must differ from origin station.")]);
        }
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

    private async Task EnsureNotDuplicatedAsync(
        Guid operatorId,
        string name,
        Guid originStationId,
        Guid destinationStationId,
        Guid? excludedRouteId,
        CancellationToken cancellationToken)
    {
        var duplicate = await routeRepository.FindDuplicateWithTransactionLockAsync(
            operatorId, name, originStationId, destinationStationId, excludedRouteId, cancellationToken);
        if (duplicate is not null)
        {
            throw new CodedConflictException(
                "ROUTE_DUPLICATED",
                "A Route with the same normalized name and station pair already exists.",
                [new ValidationError("existingRouteId", duplicate.Id.ToString("D"))]);
        }
    }
}
