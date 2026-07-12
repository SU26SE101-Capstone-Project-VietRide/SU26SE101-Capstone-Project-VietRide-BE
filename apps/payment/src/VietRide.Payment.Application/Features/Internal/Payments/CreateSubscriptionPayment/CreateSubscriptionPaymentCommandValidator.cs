using FluentValidation;

namespace VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed class CreateSubscriptionPaymentCommandValidator : AbstractValidator<CreateSubscriptionPaymentCommand>
{
    public CreateSubscriptionPaymentCommandValidator()
    {
        RuleFor(command => command.UpgradeAttemptId).NotEmpty();
        RuleFor(command => command.SubscriptionId).NotEmpty();
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.BillingPeriod).Must(period => period is "MONTHLY" or "YEARLY");
        RuleFor(command => command.Amount).GreaterThan(0).Must(amount => amount % 1000 == 0);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(command => command.ClientIpAddress).NotEmpty().MaximumLength(64);
    }
}
