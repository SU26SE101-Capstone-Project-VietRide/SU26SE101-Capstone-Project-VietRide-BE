using FluentValidation;

namespace VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;

public sealed class BatchChargePaymentCommandValidator : AbstractValidator<BatchChargePaymentCommand>
{
    public BatchChargePaymentCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Method).Equal("WALLET").WithMessage("Batch charge supports WALLET only.");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Items).NotNull().Must(x => x.Count >= 2).WithMessage("At least two charge items are required.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ReferenceType).Equal("BOOKING").WithMessage("Batch WALLET charge supports BOOKING references only.");
            item.RuleFor(x => x.ReferenceId).NotEmpty();
            item.RuleFor(x => x.Amount).GreaterThan(0);
            item.RuleFor(x => x.Context).NotNull().WithErrorCode("PAYMENT_CONTEXT_INVALID");
        });
    }
}
