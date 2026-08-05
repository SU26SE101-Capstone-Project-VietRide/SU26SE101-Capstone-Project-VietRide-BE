using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class CreateRouteChangeProposalValidator : AbstractValidator<CreateRouteChangeProposalCommand>
{
    public CreateRouteChangeProposalValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Type).NotEmpty().Must(x => string.Equals(x, "EXISTING", StringComparison.OrdinalIgnoreCase) || string.Equals(x, "CUSTOM", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IncidentId).NotEmpty().When(x => x.IncidentId.HasValue);
        RuleFor(x => x.AlternativeRouteId).NotEmpty().When(x => x.AlternativeRouteId.HasValue);
        RuleFor(x => x.CustomRoute!.Name).NotEmpty().MaximumLength(255).When(x => x.CustomRoute is not null);
        RuleFor(x => x.CustomRoute!.DestinationStationId).NotEmpty().When(x => x.CustomRoute is not null);
        RuleFor(x => x.CustomRoute!.PathPolyline).NotEmpty().When(x => x.CustomRoute is not null);
        RuleForEach(x => x.CustomRoute!.Stops).ChildRules(stop =>
        {
            stop.RuleFor(x => x.StopId).NotEmpty();
            stop.RuleFor(x => x.OrderIndex).GreaterThan(0);
            stop.RuleFor(x => x.EstimatedDurationFromOriginMinutes).GreaterThanOrEqualTo(0);
            stop.RuleFor(x => x.DistanceFromOriginKm).GreaterThanOrEqualTo(0m).When(x => x.DistanceFromOriginKm.HasValue);
        }).When(x => x.CustomRoute is not null);
    }
}
