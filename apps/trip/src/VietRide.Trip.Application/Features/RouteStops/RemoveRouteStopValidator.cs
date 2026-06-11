using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteStops;

public sealed class RemoveRouteStopValidator : AbstractValidator<RemoveRouteStopCommand>
{
    public RemoveRouteStopValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();
        RuleFor(command => command.RouteId)
            .NotEmpty();
        RuleFor(command => command.StopId)
            .NotEmpty();
    }
}
