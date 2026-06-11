using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

public sealed class CreateRouteStopFareTemplateValidator : AbstractValidator<CreateRouteStopFareTemplateCommand>
{
    public CreateRouteStopFareTemplateValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();
        RuleFor(command => command.RouteId)
            .NotEmpty();
        RuleFor(command => command.StopId)
            .NotEmpty();
        RuleFor(command => command.FareFromThisStop)
            .GreaterThanOrEqualTo(0);
        RuleFor(command => command.EffectiveFrom)
            .NotEmpty();
        RuleFor(command => command.EffectiveUntil)
            .GreaterThan(command => command.EffectiveFrom)
            .When(command => command.EffectiveUntil.HasValue);
    }
}
