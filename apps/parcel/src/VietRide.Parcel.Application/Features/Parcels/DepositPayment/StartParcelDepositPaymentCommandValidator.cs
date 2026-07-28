using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.DepositPayment;

public sealed class StartParcelDepositPaymentCommandValidator
    : AbstractValidator<StartParcelDepositPaymentCommand>
{
    public StartParcelDepositPaymentCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.SenderUserId).NotEmpty();
        RuleFor(x => x.PaymentMethod)
            .Must(method => method is "WALLET" or "VNPAY")
            .WithMessage("PaymentMethod must be WALLET or VNPAY.");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}
