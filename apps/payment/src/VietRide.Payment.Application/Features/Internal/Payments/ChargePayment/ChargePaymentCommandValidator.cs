using FluentValidation;

namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

public sealed class ChargePaymentCommandValidator : AbstractValidator<ChargePaymentCommand>
{
    public ChargePaymentCommandValidator()
    {
        RuleFor(x => x.ReferenceType)
            .Must(rt => rt is "BOOKING" or "BOOKING_GROUP" or "PARCEL" or "PARCEL_ADDITIONAL")
            .WithMessage("Charge supports BOOKING, BOOKING_GROUP, PARCEL, or PARCEL_ADDITIONAL references only.");
        RuleFor(x => x.ReferenceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method)
            .Must(method => method is "WALLET" or "VNPAY")
            .WithMessage("method must be WALLET or VNPAY.");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ClientIpAddress).NotEmpty();
        RuleFor(x => x.Context).NotNull().WithErrorCode("PAYMENT_CONTEXT_INVALID");
    }
}
