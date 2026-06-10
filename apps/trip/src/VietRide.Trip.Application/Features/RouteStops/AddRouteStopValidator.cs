using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteStops;

public sealed class AddRouteStopValidator : AbstractValidator<AddRouteStopCommand>
{
    public AddRouteStopValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();
        RuleFor(command => command.RouteId)
            .NotEmpty();
        RuleFor(command => command.StopId)
            .NotEmpty();
        RuleFor(command => command.OrderIndex)
            .GreaterThan(0);
        RuleFor(command => command.EstimatedDurationFromOriginMinutes)
            .GreaterThanOrEqualTo(0);
        RuleFor(command => command.DistanceFromOriginKm)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.DistanceFromOriginKm.HasValue);
    }
}
