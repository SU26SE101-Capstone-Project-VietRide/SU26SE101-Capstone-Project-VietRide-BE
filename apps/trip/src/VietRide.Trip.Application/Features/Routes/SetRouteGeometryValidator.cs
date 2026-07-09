using FluentValidation;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class SetRouteGeometryValidator : AbstractValidator<SetRouteGeometryCommand>
{
    public SetRouteGeometryValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.RouteId).NotEmpty();
    }
}
