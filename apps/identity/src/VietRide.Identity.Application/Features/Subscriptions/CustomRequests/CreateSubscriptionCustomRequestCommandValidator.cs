using FluentValidation;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class CreateSubscriptionCustomRequestCommandValidator
    : AbstractValidator<CreateSubscriptionCustomRequestCommand>
{
    public CreateSubscriptionCustomRequestCommandValidator()
    {
        RuleFor(command => command.CallerUserId).NotEmpty();
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.MaxVehicles).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxDrivers).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxAssistants).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxOperatorUsers).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxRoutes).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxTripsPerMonth).GreaterThanOrEqualTo(0);
        RuleFor(command => command.PreferredBillingPeriod).Must(value => value is "MONTHLY" or "YEARLY");
        RuleFor(command => command.Note).MaximumLength(2000);
    }
}
