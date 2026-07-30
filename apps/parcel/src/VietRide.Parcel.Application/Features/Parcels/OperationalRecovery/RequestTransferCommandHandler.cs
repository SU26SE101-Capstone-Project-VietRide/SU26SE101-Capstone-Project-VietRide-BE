using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class RequestTransferCommandHandler
    : IRequestHandler<RequestTransferCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public RequestTransferCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IMediator mediator,
        IClock clock)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<OperationalParcelResponse> Handle(
        RequestTransferCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(
            command.ParcelId,
            cancellationToken);
        if (parcel is null)
        {
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel '{command.ParcelId}' not found.");
        }

        if (parcel.OperatorId != command.OperatorId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Parcel does not belong to this operator.");
        }

        if (parcel.Status is not (
            ParcelStatus.PENDING_OPERATOR_ACTION
            or ParcelStatus.LOADED
            or ParcelStatus.IN_TRANSIT))
        {
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel status '{parcel.Status}' cannot request transfer.");
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Transfer reason must contain between 1 and 500 characters.");
        }

        if (parcel.TripId == command.TargetTripId)
        {
            throw new CodedConflictException(
                "INVALID_TRANSFER_TARGET",
                "Target trip must differ from current trip.");
        }

        if (parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION)
        {
            var active = await _parcelRepository.GetActiveCargoRecoveryOperationAsync(
                parcel.Id,
                cancellationToken);
            if (active is not null)
            {
                EnsureMatchingTransfer(active, parcel.TripId, command.TargetTripId);
                return await _mediator.Send(
                    new ResumeCargoRecoveryOperationCommand(active.Id),
                    cancellationToken);
            }
        }

        await ValidateTargetTripAsync(command, cancellationToken);

        if (parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION)
        {
            var operation = await ClaimTransferAsync(
                command,
                parcel.TripId,
                reason,
                cancellationToken);
            return await _mediator.Send(
                new ResumeCargoRecoveryOperationCommand(operation.Id),
                cancellationToken);
        }

        return await RequestPhysicalTransferAsync(
            parcel.Id,
            parcel.TripId,
            command,
            reason,
            cancellationToken);
    }

    private async Task ValidateTargetTripAsync(
        RequestTransferCommand command,
        CancellationToken cancellationToken)
    {
        var targetTrip = await _tripClient.GetTripParcelSnapshotAsync(
            command.TargetTripId,
            cancellationToken);
        switch (targetTrip.Kind)
        {
            case TripSnapshotOutcomeKind.TripNotFound:
                throw new CodedNotFoundException(
                    "TRIP_NOT_FOUND",
                    $"Trip '{command.TargetTripId}' not found.");
            case TripSnapshotOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    targetTrip.ErrorMessage ?? "Trip service unavailable.");
        }

        if (targetTrip.Snapshot!.OperatorId != command.OperatorId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Target trip does not belong to this operator.");
        }
    }

    private async Task<ParcelCargoRecoveryOperationSnapshot> ClaimTransferAsync(
        RequestTransferCommand command,
        Guid sourceTripId,
        string reason,
        CancellationToken cancellationToken)
    {
        ParcelCargoRecoveryOperationSnapshot? claimed;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            claimed = await _parcelRepository.TryClaimCargoRecoveryTransferAsync(
                command.IdempotencyKey,
                command.ParcelId,
                command.OperatorId,
                command.TargetTripId,
                command.ActorUserId,
                reason,
                _clock.UtcNow,
                cancellationToken);
            if (claimed is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
            else
            {
                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (claimed is not null)
        {
            return claimed;
        }

        var concurrent = await _parcelRepository.GetActiveCargoRecoveryOperationAsync(
            command.ParcelId,
            cancellationToken);
        if (concurrent is not null)
        {
            EnsureMatchingTransfer(concurrent, sourceTripId, command.TargetTripId);
            return concurrent;
        }

        throw new CodedConflictException(
            "TRIP_CARGO_TRANSFER_CONFLICT",
            "Parcel recovery transfer lost a concurrent state change.");
    }

    private async Task<OperationalParcelResponse> RequestPhysicalTransferAsync(
        Guid parcelId,
        Guid sourceTripId,
        RequestTransferCommand command,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        ParcelPaymentTransitionSnapshot snapshot;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryRequestTransferAsync(
                parcelId,
                command.OperatorId,
                command.TargetTripId,
                now,
                cancellationToken)
                ?? throw new CodedConflictException(
                    "RACE_LOST",
                    "Parcel status changed concurrently; cannot request transfer.");

            var eventId = ParcelOperationId.Create(
                command.IdempotencyKey,
                parcelId,
                "TRANSFER_INITIATED_EVENT");
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                eventId,
                ParcelOutboxEvents.TransferInitiated,
                new
                {
                    eventId,
                    occurredAt = now,
                    parcelId = snapshot.ParcelId,
                    originalTripId = sourceTripId,
                    newTripId = command.TargetTripId,
                    reason,
                },
                cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return new OperationalParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            TripId: sourceTripId,
            TransferTargetTripId: command.TargetTripId);
    }

    private static void EnsureMatchingTransfer(
        ParcelCargoRecoveryOperationSnapshot operation,
        Guid sourceTripId,
        Guid targetTripId)
    {
        if (operation.OperationType != ParcelCargoRecoveryOperationType.TRANSFER
            || operation.SourceTripId != sourceTripId
            || operation.TargetTripId != targetTripId
            || operation.TargetState != "RESERVED")
        {
            throw new CodedConflictException(
                "PARCEL_CARGO_RECOVERY_IN_PROGRESS",
                "Parcel already has a different cargo recovery operation in progress.");
        }
    }
}
