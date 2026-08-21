using FluentValidation;

namespace VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;

public sealed class QuoteSubscriptionUpgradeCommandValidator : AbstractValidator<QuoteSubscriptionUpgradeCommand>
{
    public QuoteSubscriptionUpgradeCommandValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.BillingPeriod).Must(period => period is "MONTHLY" or "YEARLY");
        RuleFor(command => command.PaymentMethod).Must(method => method is "WALLET" or "VNPAY");
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(100)
            .Must(BeUuidV4)
            .WithMessage("Idempotency-Key must be a UUID v4 value.");
    }

    private static bool BeUuidV4(string value)
        => Guid.TryParse(value, out var id) && (id.ToByteArray()[7] >> 4) == 4;
}
