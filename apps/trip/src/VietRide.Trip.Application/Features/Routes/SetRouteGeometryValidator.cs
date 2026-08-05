using FluentValidation;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class SetRouteGeometryValidator : AbstractValidator<SetRouteGeometryCommand>
{
    public SetRouteGeometryValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.RouteId).NotEmpty();
        RuleFor(command => command)
            .Must(command => command.ManualDistanceKm.HasValue == command.ManualDurationMinutes.HasValue)
            .WithName("manualMetrics")
            .WithMessage("Both manual Route metrics must be supplied together.");
        RuleFor(command => command.ManualDistanceKm)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.ManualDistanceKm.HasValue);
        RuleFor(command => command.ManualDurationMinutes)
            .GreaterThanOrEqualTo(0)
            .When(command => command.ManualDurationMinutes.HasValue);
    }
}
