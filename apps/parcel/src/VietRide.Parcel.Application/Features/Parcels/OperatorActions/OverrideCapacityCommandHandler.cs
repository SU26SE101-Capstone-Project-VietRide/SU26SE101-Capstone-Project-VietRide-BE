using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorActions;

public sealed class OverrideCapacityCommandHandler
    : IRequestHandler<OverrideCapacityCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public OverrideCapacityCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<OperationalParcelResponse> Handle(
        OverrideCapacityCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new CodedValidationException("VALIDATION_ERROR", "Override reason is required.");

        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (parcel.Status != ParcelStatus.PENDING_OPERATOR_ACTION
            || parcel.PendingActionType is not (PendingActionType.CAPACITY_EXCEEDED or PendingActionType.RESERVE_FAILED))
        {
            throw new CodedConflictException(
                "INVALID_PENDING_ACTION",
                "Parcel is not waiting for a capacity override.");
        }

        var weightKg = parcel.ActualWeightKg ?? parcel.EstimatedWeightKg;
        var volumeM3 = parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3;
        var actionType = parcel.PendingActionType.Value;
        var cargoOutcome = actionType == PendingActionType.RESERVE_FAILED
            ? await _tripClient.ReserveCargoWithOverrideAsync(
                parcel.TripId,
                parcel.Id,
                weightKg,
                volumeM3,
                command.IdempotencyKey ?? parcel.Id,
                cancellationToken)
            : await _tripClient.RemeasureCargoAsync(
                parcel.TripId,
                parcel.Id,
                weightKg,
                volumeM3,
                allowCapacityOverflow: true,
                command.IdempotencyKey ?? parcel.Id,
                cancellationToken);

        if (cargoOutcome.Kind != TripCargoOutcomeKind.Success)
        {
            throw cargoOutcome.Kind switch
            {
                TripCargoOutcomeKind.TripNotFound => new ParcelDependencyUnavailableException(
                    "TRIP_NOT_FOUND",
                    cargoOutcome.ErrorMessage ?? "Trip was not found."),
                TripCargoOutcomeKind.CapacityExceeded => new CodedConflictException(
                    "TRIP_CARGO_CAPACITY_EXCEEDED",
                    cargoOutcome.ErrorMessage ?? "Trip cargo capacity would be exceeded."),
                _ => new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    cargoOutcome.ErrorMessage ?? "Trip service unavailable."),
            };
        }

        var now = DateTimeOffset.UtcNow;
        ParcelPaymentTransitionSnapshot snapshot;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryResolvePendingOperatorActionAsync(
                command.ParcelId,
                actionType,
                now,
                cancellationToken)
                ?? throw new CodedConflictException("RACE_LOST", "Parcel pending action changed concurrently.");

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                "parcel.capacity_overridden",
                new
                {
                    parcelId = snapshot.ParcelId,
                    parcelCode = snapshot.ParcelCode,
                    operatorId = snapshot.OperatorId,
                    actorUserId = command.ActorUserId,
                    pendingActionType = actionType.ToString(),
                    weightKg,
                    volumeM3,
                    reason = command.Reason.Trim(),
                    occurredAt = now,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
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
            TripId: snapshot.TripId);
    }
}
