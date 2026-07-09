using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorActions;

public sealed class ConfirmRefundCommandHandler
    : IRequestHandler<ConfirmRefundCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmRefundCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<OperationalParcelResponse> Handle(
        ConfirmRefundCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (parcel.Status != ParcelStatus.PENDING_OPERATOR_ACTION
            || parcel.PendingActionType != PendingActionType.REFUND_CONFIRMATION)
        {
            throw new CodedConflictException(
                "INVALID_PENDING_ACTION",
                "Parcel is not waiting for refund confirmation.");
        }

        if (parcel.RefundAmount.Amount <= 0)
            throw new CodedConflictException("INVALID_REFUND_AMOUNT", "Parcel has no refund amount to confirm.");

        var now = DateTimeOffset.UtcNow;
        ParcelPaymentTransitionSnapshot snapshot;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryResolvePendingOperatorActionAsync(
                command.ParcelId,
                PendingActionType.REFUND_CONFIRMATION,
                now,
                cancellationToken)
                ?? throw new CodedConflictException("RACE_LOST", "Parcel pending action changed concurrently.");

            await ParcelOutboxEvents.EnqueueRefundAsync(
                _outbox,
                snapshot.ParcelId,
                snapshot.SenderUserId,
                parcel.RefundAmount.Amount,
                cancellationToken);

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                "parcel.refund_confirmed",
                new
                {
                    parcelId = snapshot.ParcelId,
                    parcelCode = snapshot.ParcelCode,
                    operatorId = snapshot.OperatorId,
                    actorUserId = command.ActorUserId,
                    refundAmount = parcel.RefundAmount.Amount,
                    reason = command.Reason?.Trim(),
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
