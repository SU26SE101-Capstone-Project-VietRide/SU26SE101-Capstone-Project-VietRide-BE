using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.FinalPayment;

public sealed class StartParcelFinalPaymentCommandHandler
    : IRequestHandler<StartParcelFinalPaymentCommand, ParcelFinalPaymentResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IPaymentServiceClient _payments;
    private readonly IClock _clock;

    public StartParcelFinalPaymentCommandHandler(
        IParcelRepository parcels,
        IPaymentServiceClient payments,
        IClock clock)
    {
        _parcels = parcels;
        _payments = payments;
        _clock = clock;
    }

    public async Task<ParcelFinalPaymentResponse> Handle(
        StartParcelFinalPaymentCommand command,
        CancellationToken cancellationToken)
    {
        GuardMobileReturnMode(command.PaymentMethod, command.PaymentReturnMode);

        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");
        if (parcel.SenderUserId != command.SenderUserId)
            throw new ForbiddenException("FORBIDDEN", "Only the sender can pay the parcel balance.");
        if (parcel.Status != ParcelStatus.PENDING_FINAL_PAYMENT)
            throw new CodedConflictException("INVALID_STATUS", $"Parcel is in status '{parcel.Status}'.");
        if (parcel.BalancePaymentId.HasValue)
            throw new CodedConflictException("PAYMENT_ALREADY_STARTED", "Final payment has already been started.");

        var now = _clock.UtcNow;
        if (!parcel.FinalPaymentDeadline.HasValue || now >= parcel.FinalPaymentDeadline.Value)
        {
            throw new CodedConflictException(
                "FINAL_PAYMENT_DEADLINE_PASSED",
                "The parcel final-payment deadline has passed.");
        }

        var balanceDue = Math.Max(
            0,
            parcel.BalanceRequiredVnd.Amount - parcel.BalancePaidVnd.Amount);
        if (balanceDue <= 0)
            throw new CodedConflictException("BALANCE_ALREADY_PAID", "The parcel balance is already fully paid.");

        var outcome = await _payments.ChargeParcelPaymentAsync(
            "PARCEL_ADDITIONAL",
            parcel.Id,
            parcel.SenderUserId,
            balanceDue,
            command.PaymentMethod,
            command.IdempotencyKey,
            cancellationToken,
            CreatePaymentContext(parcel, balanceDue),
            parcel.FinalPaymentDeadline.Value,
            command.PaymentReturnMode);

        if (outcome.Kind == ChargeOutcomeKind.InsufficientFunds)
        {
            throw new CodedValidationException(
                "INSUFFICIENT_FUNDS",
                outcome.ErrorMessage ?? "Insufficient wallet balance.");
        }

        if (outcome.Kind != ChargeOutcomeKind.Success || outcome.Result is null)
        {
            if (outcome.ErrorStatusCode == 503
                && string.Equals(outcome.ErrorCode, "VNPAY_MOBILE_SDK_DISABLED", StringComparison.Ordinal))
            {
                throw new ParcelPaymentReturnModeException(
                    503,
                    "VNPAY_MOBILE_SDK_DISABLED",
                    outcome.ErrorMessage ?? "VNPay Mobile SDK is disabled.");
            }

            throw new ParcelDependencyUnavailableException(
                "PAYMENT_SERVICE_ERROR",
                outcome.ErrorMessage ?? "Payment service unavailable.");
        }

        var assigned = await _parcels.TryAssignBalancePaymentIdAsync(
            parcel.Id,
            outcome.Result.PaymentId,
            now,
            cancellationToken);
        if (!assigned)
            throw new CodedConflictException("RACE_LOST", "Parcel status changed while starting final payment.");

        return new ParcelFinalPaymentResponse(
            parcel.Id,
            ParcelStatus.PENDING_FINAL_PAYMENT.ToString(),
            outcome.Result.PaymentId,
            parcel.BalanceRequiredVnd.Amount,
            parcel.BalancePaidVnd.Amount,
            parcel.FinalPaymentDeadline.Value,
            outcome.Result.PaymentRedirectUrl,
            outcome.Result.PaymentReturnMode,
            outcome.Result.VnPaySdk);
    }

    private static PaymentContextSnapshot CreatePaymentContext(
        Domain.Entities.Parcel parcel,
        long balanceDue)
        => new(1,
        [
            new PaymentAllocationSnapshot(
                parcel.Id,
                "PARCEL_ADDITIONAL",
                parcel.OperatorId,
                parcel.TripId,
                balanceDue,
                0,
                0,
                parcel.ParcelCode),
        ]);

    private static void GuardMobileReturnMode(string paymentMethod, string? paymentReturnMode)
    {
        if (!string.Equals(paymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(paymentReturnMode))
        {
            throw new ParcelPaymentReturnModeException(
                426,
                "MOBILE_APP_UPDATE_REQUIRED",
                "Update the mobile app to continue with VNPay.");
        }

        if (!string.Equals(paymentReturnMode, "MOBILE_SDK", StringComparison.OrdinalIgnoreCase))
        {
            throw new ParcelPaymentReturnModeException(
                422,
                "PAYMENT_RETURN_MODE_INVALID",
                "paymentReturnMode must be MOBILE_SDK.");
        }
    }
}
