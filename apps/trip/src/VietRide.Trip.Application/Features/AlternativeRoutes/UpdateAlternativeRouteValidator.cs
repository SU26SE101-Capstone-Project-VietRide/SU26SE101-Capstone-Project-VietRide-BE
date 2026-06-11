using FluentValidation;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class UpdateAlternativeRouteValidator : AbstractValidator<UpdateAlternativeRouteCommand>
{
    public UpdateAlternativeRouteValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.AlternativeRouteId).NotEmpty();
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(255)
            .When(command => command.HasName);
        RuleFor(command => command.DestinationStationId)
            .NotEmpty()
            .When(command => command.HasDestinationStationId);
        RuleFor(command => command.TotalDistanceKm)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.HasTotalDistanceKm && command.TotalDistanceKm.HasValue);
        RuleFor(command => command.EstimatedDurationMinutes)
            .GreaterThanOrEqualTo(0)
            .When(command => command.HasEstimatedDurationMinutes && command.EstimatedDurationMinutes.HasValue);
        RuleFor(command => command.Stops)
            .NotNull()
            .When(command => command.HasStops);
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
