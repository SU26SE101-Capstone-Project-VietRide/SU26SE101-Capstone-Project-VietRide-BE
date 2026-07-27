using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels.Review;

public sealed class ReviewParcelCommandHandler
    : IRequestHandler<ReviewParcelCommand, ReviewParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public ReviewParcelCommandHandler(
        IParcelRepository parcelRepository,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<ReviewParcelResponse> Handle(
        ReviewParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (!Enum.TryParse<ParcelReviewDecision>(command.Decision, ignoreCase: true, out var decision))
            throw new CodedValidationException("INVALID_DECISION", $"'{command.Decision}' is not valid. Must be APPROVED or REJECTED.");

        var now = DateTimeOffset.UtcNow;

        if (decision == ParcelReviewDecision.APPROVED)
        {
            if (parcel.Status != ParcelStatus.PENDING_OPERATOR_REVIEW)
                throw new CodedConflictException(
                    "INVALID_STATUS",
                    $"Parcel is in status '{parcel.Status}'. Only PENDING_OPERATOR_REVIEW can be approved.");

            if (parcel.ReviewDecision != ParcelReviewDecision.PENDING)
                throw new CodedConflictException(
                    "ALREADY_REVIEWED",
                    $"Parcel has already been reviewed with decision '{parcel.ReviewDecision}'.");

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            ParcelPaymentTransitionSnapshot snapshot;
            try
            {
                snapshot = await _parcelRepository.TryApproveReviewAsync(
                    command.ParcelId, command.ReviewedByUserId, parcel.DepositRequiredVnd, now, cancellationToken)
                    ?? throw new CodedConflictException(
                        "RACE_LOST", "Parcel was already reviewed or status changed.");
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }

            return new ReviewParcelResponse(
                command.ParcelId,
                snapshot.ParcelCode,
                ParcelStatus.PENDING_PAYMENT.ToString(),
                parcel.DepositRequiredVnd.Amount);
        }
        else // REJECTED
        {
            if (parcel.Status != ParcelStatus.PENDING_OPERATOR_REVIEW)
                throw new CodedConflictException(
                    "INVALID_STATUS",
                    $"Parcel is in status '{parcel.Status}'. Only PENDING_OPERATOR_REVIEW can be rejected.");

            if (parcel.ReviewDecision != ParcelReviewDecision.PENDING)
                throw new CodedConflictException(
                    "ALREADY_REVIEWED",
                    $"Parcel has already been reviewed with decision '{parcel.ReviewDecision}'.");

            if (string.IsNullOrWhiteSpace(command.Reason))
                throw new CodedValidationException(
                    "VALIDATION_ERROR", "Reason is required for REJECTED decision.");

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            ParcelPaymentTransitionSnapshot snapshot;
            try
            {
                snapshot = await _parcelRepository.TryRejectReviewAsync(
                    command.ParcelId, command.ReviewedByUserId, command.Reason, now, cancellationToken)
                    ?? throw new CodedConflictException(
                        "RACE_LOST", "Parcel was already reviewed or status changed.");
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.Rejected,
                    new { parcelId = snapshot.ParcelId },
                    cancellationToken);
                await _statsRepository.UpsertIncrementAsync(
                    snapshot.OperatorId,
                    DateOnly.FromDateTime(now.UtcDateTime),
                    0, 0, 0, 1, 0, 0, 0,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }

            return new ReviewParcelResponse(
                command.ParcelId,
                snapshot.ParcelCode,
                ParcelStatus.REJECTED.ToString(),
                null);
        }
    }

}
