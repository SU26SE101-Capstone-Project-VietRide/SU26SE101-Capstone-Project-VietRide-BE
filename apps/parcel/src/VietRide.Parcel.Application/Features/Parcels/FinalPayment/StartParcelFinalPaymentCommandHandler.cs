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
            parcel.FinalPaymentDeadline.Value);

        if (outcome.Kind == ChargeOutcomeKind.InsufficientFunds)
        {
            throw new CodedValidationException(
                "INSUFFICIENT_FUNDS",
                outcome.ErrorMessage ?? "Insufficient wallet balance.");
        }

        if (outcome.Kind != ChargeOutcomeKind.Success || outcome.Result is null)
        {
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
            outcome.Result.PaymentRedirectUrl);
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
                0),
        ]);
}
