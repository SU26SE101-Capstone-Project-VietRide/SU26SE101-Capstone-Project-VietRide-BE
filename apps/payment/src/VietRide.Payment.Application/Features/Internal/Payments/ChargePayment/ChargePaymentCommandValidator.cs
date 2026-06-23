using FluentValidation;

namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

public sealed class ChargePaymentCommandValidator : AbstractValidator<ChargePaymentCommand>
{
    public ChargePaymentCommandValidator()
    {
        RuleFor(x => x.ReferenceType).Equal("BOOKING").WithMessage("Charge supports BOOKING references only.");
        RuleFor(x => x.ReferenceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method)
            .Must(method => method is "WALLET" or "VNPAY")
            .WithMessage("method must be WALLET or VNPAY.");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ClientIpAddress).NotEmpty();
    }
}
