using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.FinalPayment;

public sealed class StartParcelFinalPaymentCommandValidator
    : AbstractValidator<StartParcelFinalPaymentCommand>
{
    public StartParcelFinalPaymentCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.SenderUserId).NotEmpty();
        RuleFor(x => x.PaymentMethod)
            .Must(method => method is "WALLET" or "VNPAY")
            .WithMessage("PaymentMethod must be WALLET or VNPAY.");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}
