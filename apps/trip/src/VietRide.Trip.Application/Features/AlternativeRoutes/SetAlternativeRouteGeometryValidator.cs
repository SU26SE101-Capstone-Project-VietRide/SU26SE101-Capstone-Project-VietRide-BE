using FluentValidation;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class SetAlternativeRouteGeometryValidator : AbstractValidator<SetAlternativeRouteGeometryCommand>
{
    public SetAlternativeRouteGeometryValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.AlternativeRouteId).NotEmpty();
    }
}
