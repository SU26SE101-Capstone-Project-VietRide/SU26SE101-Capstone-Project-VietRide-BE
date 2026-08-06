using FluentValidation;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class UpsertFullRouteValidator : AbstractValidator<UpsertFullRouteCommand>
{
    public UpsertFullRouteValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.RouteId).NotEqual(Guid.Empty).When(command => command.RouteId.HasValue);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(255);
        RuleFor(command => command.OriginStationId).NotEmpty();
        RuleFor(command => command.DestinationStationId).NotEmpty();
        RuleFor(command => command.BaseFare).GreaterThanOrEqualTo(0);
        RuleFor(command => command)
            .Must(command => command.OriginStationId != command.DestinationStationId)
            .WithName("destinationStationId")
            .WithMessage("Destination station must differ from origin station.");
        RuleFor(command => command)
            .Must(command => command.ManualDistanceKm.HasValue == command.ManualDurationMinutes.HasValue)
            .WithName("manualMetrics")
            .WithMessage("Both manual metrics must be supplied together.");
        RuleFor(command => command)
            .Must(command => command.PathPolyline is not null || command.ManualDistanceKm.HasValue)
            .When(command => !command.RouteId.HasValue)
            .WithName("manualMetrics")
            .WithMessage("Manual metrics are required when a new Route has no polyline.");
        RuleFor(command => command.ManualDistanceKm).GreaterThanOrEqualTo(0m).When(command => command.ManualDistanceKm.HasValue);
        RuleFor(command => command.ManualDurationMinutes).GreaterThanOrEqualTo(0).When(command => command.ManualDurationMinutes.HasValue);
    }
}
