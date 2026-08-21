using FluentValidation;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class ApproveSubscriptionCustomRequestCommandValidator
    : AbstractValidator<ApproveSubscriptionCustomRequestCommand>
{
    public ApproveSubscriptionCustomRequestCommandValidator()
    {
        RuleFor(command => command.CallerUserId).NotEmpty();
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Description).MaximumLength(2000);
        RuleFor(command => command.PricePerMonth).GreaterThanOrEqualTo(0).Must(value => value % 1000 == 0);
        RuleFor(command => command.PricePerYear).GreaterThanOrEqualTo(0).Must(value => value % 1000 == 0);
        RuleFor(command => command)
            .Must(command => command.PricePerMonth > 0 || command.PricePerYear > 0)
            .WithMessage("At least one monthly or yearly price must be greater than zero.");
        RuleFor(command => command.MaxVehicles).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxDrivers).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxAssistants).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxOperatorUsers).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxRoutes).GreaterThanOrEqualTo(0);
        RuleFor(command => command.MaxTripsPerMonth).GreaterThanOrEqualTo(0);
    }
}
