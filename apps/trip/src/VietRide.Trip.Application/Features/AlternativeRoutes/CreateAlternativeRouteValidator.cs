using FluentValidation;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class CreateAlternativeRouteValidator : AbstractValidator<CreateAlternativeRouteCommand>
{
    public CreateAlternativeRouteValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.RouteId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(255);
        RuleFor(command => command.DestinationStationId).NotEmpty();
        RuleFor(command => command.TotalDistanceKm)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.TotalDistanceKm.HasValue);
        RuleFor(command => command.EstimatedDurationMinutes)
            .GreaterThanOrEqualTo(0)
            .When(command => command.EstimatedDurationMinutes.HasValue);
        RuleFor(command => command.Stops).NotNull();
        RuleForEach(command => command.Stops).ChildRules(stop =>
        {
            stop.RuleFor(x => x.StopId).NotEmpty();
            stop.RuleFor(x => x.OrderIndex).GreaterThan(0);
            stop.RuleFor(x => x.EstimatedDurationFromOriginMinutes).GreaterThanOrEqualTo(0);
            stop.RuleFor(x => x.DistanceFromOriginKm)
                .GreaterThanOrEqualTo(0m)
                .When(x => x.DistanceFromOriginKm.HasValue);
        });
    }
}
