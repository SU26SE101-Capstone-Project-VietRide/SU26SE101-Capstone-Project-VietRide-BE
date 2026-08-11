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

public sealed class ResumeCargoRecoveryOperationCommandHandler
    : IRequestHandler<ResumeCargoRecoveryOperationCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ResumeCargoRecoveryOperationCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<OperationalParcelResponse> Handle(
        ResumeCargoRecoveryOperationCommand command,
        CancellationToken cancellationToken)
    {
        var operation = await _parcelRepository.GetCargoRecoveryOperationAsync(
            command.OperationId,
            cancellationToken)
            ?? throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Cargo recovery operation '{command.OperationId}' not found.");

        if (operation.OperationStatus == ParcelCargoRecoveryOperationStatus.COMPLETED)
        {
            return ToCompletedResponse(operation);
        }

        if (operation.OperationStatus == ParcelCargoRecoveryOperationStatus.FAILED)
        {
            throw new CodedConflictException(
                operation.FailureCode ?? "TRIP_CARGO_TRANSFER_CONFLICT",
                "The cargo recovery operation has already failed definitively.");
        }

        return operation.OperationType switch
        {
            ParcelCargoRecoveryOperationType.TRANSFER
                => await ExecuteTransferAsync(operation, cancellationToken),
            ParcelCargoRecoveryOperationType.RETURN
                => await ExecuteReturnAsync(operation, cancellationToken),
            ParcelCargoRecoveryOperationType.RELEASE
                => await ExecuteReleaseAsync(operation, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported cargo recovery operation '{operation.OperationType}'."),
        };
    }

    private async Task<OperationalParcelResponse> ExecuteTransferAsync(
        ParcelCargoRecoveryOperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        if (operation.TargetTripId is null || operation.TargetState != "RESERVED")
        {
            throw new CodedConflictException(
                "TRIP_CARGO_TRANSFER_CONFLICT",
                "Persisted cargo transfer operation is incomplete.");
        }

        var outcome = await _tripClient.TransferCargoAsync(
            operation.SourceTripId,
            operation.ParcelId,
            operation.TargetTripId.Value,
            operation.TargetState,
            allowCapacityOverflow: false,
            operation.Id,
            cancellationToken);

        if (outcome.Kind == TripCargoTransferOutcomeKind.Success)
        {
            return await CompleteTransferAsync(operation, cancellationToken);
        }

        if (outcome.Kind == TripCargoTransferOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip cargo transfer outcome is unknown.");
        }

        var (failureCode, exception) = outcome.Kind switch
        {
            TripCargoTransferOutcomeKind.TripNotFound => (
                "TRIP_NOT_FOUND",
                (Exception)new CodedNotFoundException(
                    "TRIP_NOT_FOUND",
                    outcome.ErrorMessage ?? "The source or target Trip was not found.")),
            TripCargoTransferOutcomeKind.ParcelCargoNotFound => (
                "PARCEL_CARGO_NOT_FOUND",
                new CodedNotFoundException(
                    "PARCEL_CARGO_NOT_FOUND",
                    outcome.ErrorMessage ?? "The source Trip has no active cargo ledger for this Parcel.")),
            TripCargoTransferOutcomeKind.Conflict => (
                "TRIP_CARGO_TRANSFER_CONFLICT",
                new CodedConflictException(
                    "TRIP_CARGO_TRANSFER_CONFLICT",
                    outcome.ErrorMessage ?? "Trip cargo transfer lost a concurrent mutation.")),
            TripCargoTransferOutcomeKind.CapacityExceeded => (
                "TRIP_CARGO_CAPACITY_EXCEEDED",
                new CodedValidationException(
                    "TRIP_CARGO_CAPACITY_EXCEEDED",
                    outcome.ErrorMessage ?? "Target Trip cargo capacity would be exceeded.")),
            _ => throw new InvalidOperationException("Unexpected Trip cargo transfer outcome."),
        };
        await FailAsync(operation.Id, failureCode, cancellationToken);
        throw exception;
    }

    private async Task<OperationalParcelResponse> ExecuteReturnAsync(
        ParcelCargoRecoveryOperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        var outcome = await _tripClient.ReleaseCargoAsync(
            operation.SourceTripId,
            operation.ParcelId,
            Positive(operation.WeightKg),
            Positive(operation.VolumeM3),
            operation.Id,
            cancellationToken);

        if (outcome.Kind == TripCargoOutcomeKind.Success)
        {
            return await CompleteReturnAsync(operation, cancellationToken);
        }

        if (outcome.Kind == TripCargoOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip cargo release outcome is unknown.");
        }

        var failureCode = outcome.Kind == TripCargoOutcomeKind.TripNotFound
            ? "TRIP_NOT_FOUND"
            : "TRIP_CARGO_TRANSFER_CONFLICT";
        await FailAsync(operation.Id, failureCode, cancellationToken);
        throw new ParcelDependencyUnavailableException(
            outcome.Kind == TripCargoOutcomeKind.TripNotFound
                ? "TRIP_SERVICE_UNAVAILABLE"
                : "TRIP_CARGO_TRANSFER_CONFLICT",
            outcome.ErrorMessage ?? "Trip cargo release was rejected.");
    }

    private async Task<OperationalParcelResponse> ExecuteReleaseAsync(
        ParcelCargoRecoveryOperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        var outcome = await _tripClient.ReleaseCargoAsync(
            operation.SourceTripId,
            operation.ParcelId,
            Positive(operation.WeightKg),
            Positive(operation.VolumeM3),
            operation.Id,
            cancellationToken);
        if (outcome.Kind != TripCargoOutcomeKind.Success)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip cargo release remains pending.");
        }

        var completed = await _parcelRepository.TryCompleteCargoRecoveryReleaseAsync(
            operation.Id,
            _clock.UtcNow,
            cancellationToken);
        if (!completed)
        {
            var replay = await _parcelRepository.GetCargoRecoveryOperationAsync(
                operation.Id,
                cancellationToken);
            if (replay?.OperationStatus != ParcelCargoRecoveryOperationStatus.COMPLETED)
            {
                throw new CodedConflictException(
                    "TRIP_CARGO_TRANSFER_CONFLICT",
                    "Parcel cargo release completion lost a concurrent mutation.");
            }

            operation = replay;
        }

        return ToCompletedResponse(operation);
    }

    private async Task<OperationalParcelResponse> CompleteTransferAsync(
        ParcelCargoRecoveryOperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        ParcelPaymentTransitionSnapshot? completed;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            completed = await _parcelRepository.TryCompleteCargoRecoveryTransferAsync(
                operation.Id,
                now,
                cancellationToken);
            if (completed is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
            else
            {
                var eventId = ParcelOperationId.Create(
                    operation.Id,
                    operation.ParcelId,
                    "TRANSFER_INITIATED_EVENT");
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    eventId,
                    ParcelOutboxEvents.TransferInitiated,
                    new
                    {
                        eventId,
                        occurredAt = now,
                        parcelId = operation.ParcelId,
                        originalTripId = operation.SourceTripId,
                        newTripId = operation.TargetTripId,
                        reason = operation.Reason,
                    },
                    cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (completed is null)
        {
            return await ResolveConcurrentCompletionAsync(operation.Id, cancellationToken);
        }

        return new OperationalParcelResponse(
            completed.ParcelId,
            completed.ParcelCode,
            completed.Status.ToString(),
            TripId: operation.TargetTripId);
    }

    private async Task<OperationalParcelResponse> CompleteReturnAsync(
        ParcelCargoRecoveryOperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        ParcelPaymentTransitionSnapshot? completed;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            completed = await _parcelRepository.TryCompleteCargoRecoveryReturnAsync(
                operation.Id,
                now,
                cancellationToken);
            if (completed is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
            else
            {
                await EnqueueReturnSideEffectsAsync(operation, completed, now, cancellationToken);
                await _statsRepository.UpsertIncrementAsync(
                    operation.OperatorId,
                    VietRide.Shared.Kernel.Time.BusinessTime.ToLocalDate(now),
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                    operation.RefundAmountVnd,
                    cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (completed is null)
        {
            return await ResolveConcurrentCompletionAsync(operation.Id, cancellationToken);
        }

        return new OperationalParcelResponse(
            completed.ParcelId,
            completed.ParcelCode,
            completed.Status.ToString(),
            TripId: operation.SourceTripId,
            ReturnReason: operation.Reason,
            ReturnedAt: now,
            RefundAmount: operation.RefundAmountVnd);
    }

    private async Task EnqueueReturnSideEffectsAsync(
        ParcelCargoRecoveryOperationSnapshot operation,
        ParcelPaymentTransitionSnapshot completed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ParcelOutboxEvents.EnqueueTerminalAsync(
            _outbox,
            ParcelOperationId.Create(operation.Id, operation.ParcelId, "OPERATOR_RETURN_TERMINAL"),
            now,
            ParcelOutboxEvents.Returned,
            completed.ParcelId,
            completed.ParcelCode,
            completed.OperatorId,
            completed.SenderUserId,
            operation.SourceTripId,
            operation.RefundAmountVnd,
            operation.Reason,
            cancellationToken);

        if (operation.RefundAmountVnd > 0)
        {
            await ParcelOutboxEvents.EnqueueCanonicalRefundAsync(
                _outbox,
                ParcelOperationId.Create(
                    operation.Id,
                    operation.ParcelId,
                    "OPERATOR_RETURN_REFUND_EVENT"),
                now,
                completed.ParcelId,
                completed.SenderUserId,
                operation.RefundAmountVnd,
                "OPERATOR_RETURN",
                ParcelOperationId.Create(
                    operation.Id,
                    operation.ParcelId,
                    "OPERATOR_RETURN_REFUND"),
                cancellationToken);
        }

        if (operation.IsStatusOverride)
        {
            var eventId = ParcelOperationId.Create(
                operation.Id,
                operation.ParcelId,
                "OPERATOR_RETURN_STATUS_OVERRIDE");
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                eventId,
                ParcelOutboxEvents.StatusOverridden,
                new
                {
                    eventId,
                    occurredAt = now,
                    parcelId = completed.ParcelId,
                    operatorId = completed.OperatorId,
                    actorUserId = operation.ActorUserId,
                    fromStatus = operation.SourceStatus.ToString(),
                    toStatus = completed.Status.ToString(),
                    reason = operation.Reason,
                    timestamp = now,
                },
                cancellationToken);
        }
    }

    private async Task FailAsync(
        Guid operationId,
        string failureCode,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var failed = await _parcelRepository.TryFailCargoRecoveryOperationAsync(
                operationId,
                failureCode,
                _clock.UtcNow,
                cancellationToken);
            if (failed)
            {
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            else
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<OperationalParcelResponse> ResolveConcurrentCompletionAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var replay = await _parcelRepository.GetCargoRecoveryOperationAsync(
            operationId,
            cancellationToken);
        if (replay?.OperationStatus == ParcelCargoRecoveryOperationStatus.COMPLETED)
        {
            return ToCompletedResponse(replay);
        }

        throw new CodedConflictException(
            "TRIP_CARGO_TRANSFER_CONFLICT",
            "Parcel cargo recovery completion lost a concurrent mutation.");
    }

    private static OperationalParcelResponse ToCompletedResponse(
        ParcelCargoRecoveryOperationSnapshot operation)
        => operation.OperationType switch
        {
            ParcelCargoRecoveryOperationType.TRANSFER => new OperationalParcelResponse(
                operation.ParcelId,
                operation.ParcelCode,
                operation.ParcelStatus.ToString(),
                TripId: operation.ParcelTripId),
            ParcelCargoRecoveryOperationType.RETURN => new OperationalParcelResponse(
                operation.ParcelId,
                operation.ParcelCode,
                operation.ParcelStatus.ToString(),
                TripId: operation.ParcelTripId,
                ReturnReason: operation.Reason,
                ReturnedAt: operation.ReturnedAt,
                RefundAmount: operation.RefundAmountVnd),
            ParcelCargoRecoveryOperationType.RELEASE => new OperationalParcelResponse(
                operation.ParcelId,
                operation.ParcelCode,
                operation.ParcelStatus.ToString(),
                TripId: operation.ParcelTripId),
            _ => throw new InvalidOperationException(
                $"Unsupported cargo recovery operation '{operation.OperationType}'."),
        };

    private static decimal Positive(decimal value)
        => value > 0 ? value : 0.0001m;
}
