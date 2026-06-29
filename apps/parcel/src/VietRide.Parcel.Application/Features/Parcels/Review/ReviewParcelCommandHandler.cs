using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels.Review;

public sealed class ReviewParcelCommandHandler
    : IRequestHandler<ReviewParcelCommand, ReviewParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IPaymentServiceClient _paymentClient;

    public ReviewParcelCommandHandler(
        IParcelRepository parcelRepository,
        IPaymentServiceClient paymentClient)
    {
        _parcelRepository = parcelRepository;
        _paymentClient = paymentClient;
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

            if (!command.DepositAmount.HasValue || command.DepositAmount.Value <= 0)
                throw new CodedValidationException(
                    "VALIDATION_ERROR", "Deposit amount is required and must be positive for APPROVED decision.");

            var floored = (command.DepositAmount.Value / 1000) * 1000;
            if (floored < 1000)
                throw new CodedValidationException(
                    "VALIDATION_ERROR", "Deposit amount must be at least 1000 VND after flooring.");

            if (command.PaymentMethod is not ("WALLET" or "VNPAY"))
                throw new CodedValidationException(
                    "VALIDATION_ERROR", "PaymentMethod must be WALLET or VNPAY.");

            var depositAmount = Money.FromRaw(floored);
            var snapshot = await _parcelRepository.TryApproveReviewAsync(
                command.ParcelId, command.ReviewedByUserId, depositAmount, now, cancellationToken);

            if (snapshot is null)
                throw new CodedConflictException(
                    "RACE_LOST", "Parcel was already reviewed or status changed.");

            // Initiate payment after atomic status transition
            var idempotencyKey = $"parcel:deposit:{command.ParcelId}";
            var outcome = await _paymentClient.ChargeParcelPaymentAsync(
                "PARCEL",
                command.ParcelId,
                parcel.SenderUserId,
                depositAmount.Amount,
                command.PaymentMethod,
                idempotencyKey,
                cancellationToken);

            if (outcome.Kind == ChargeOutcomeKind.InsufficientFunds)
            {
                throw new CodedValidationException(
                    "INSUFFICIENT_FUNDS",
                    outcome.ErrorMessage ?? "Insufficient wallet balance.");
            }

            if (outcome.Kind == ChargeOutcomeKind.TransportError)
            {
                throw new ParcelDependencyUnavailableException(
                    "PAYMENT_SERVICE_ERROR",
                    outcome.ErrorMessage ?? "Payment service unavailable.");
            }

            return new ReviewParcelResponse(
                command.ParcelId,
                snapshot.ParcelCode,
                ParcelStatus.PENDING_PAYMENT.ToString(),
                depositAmount.Amount,
                outcome.Result?.PaymentRedirectUrl);
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

            var snapshot = await _parcelRepository.TryRejectReviewAsync(
                command.ParcelId, command.ReviewedByUserId, command.Reason, now, cancellationToken);

            if (snapshot is null)
                throw new CodedConflictException(
                    "RACE_LOST", "Parcel was already reviewed or status changed.");

            return new ReviewParcelResponse(
                command.ParcelId,
                snapshot.ParcelCode,
                ParcelStatus.REJECTED.ToString(),
                null, null);
        }
    }
}
