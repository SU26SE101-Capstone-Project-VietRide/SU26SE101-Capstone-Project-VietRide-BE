using FluentValidation;

namespace VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;

public sealed class UpgradeSubscriptionCommandValidator : AbstractValidator<UpgradeSubscriptionCommand>
{
    public UpgradeSubscriptionCommandValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.BillingPeriod).Must(period => period is "MONTHLY" or "YEARLY");
        RuleFor(command => command.PaymentMethod).Must(method => method is "WALLET" or "VNPAY");
        RuleFor(command => command.ReturnUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                && parsed.Scheme is "https" or "http")
            .When(command => command.PaymentMethod == "VNPAY");
        RuleFor(command => command.ReturnUrl)
            .Empty()
            .When(command => command.PaymentMethod == "WALLET");
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(command => command.ClientIpAddress).NotEmpty().MaximumLength(64);
    }
}
