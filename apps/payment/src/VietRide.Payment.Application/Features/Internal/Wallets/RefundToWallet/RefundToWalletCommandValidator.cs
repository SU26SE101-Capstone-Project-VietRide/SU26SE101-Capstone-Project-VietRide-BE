using FluentValidation;

namespace VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;

public sealed class RefundToWalletCommandValidator : AbstractValidator<RefundToWalletCommand>
{
    public RefundToWalletCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.ReferenceType)
            .Must(value => value is "BOOKING_REFUND" or "PARCEL_REFUND")
            .WithMessage("Refund supports BOOKING_REFUND or PARCEL_REFUND references only.");
        RuleFor(x => x.ReferenceId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}
