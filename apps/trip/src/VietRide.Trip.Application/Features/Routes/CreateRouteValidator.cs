using FluentValidation;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class CreateRouteValidator : AbstractValidator<CreateRouteCommand>
{
    public CreateRouteValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(255);
        RuleFor(command => command.OriginStationId)
            .NotEmpty();
        RuleFor(command => command.DestinationStationId)
            .NotEmpty();
        RuleFor(command => command.ReturnRouteId)
            .NotEmpty()
            .When(command => command.ReturnRouteId.HasValue);
        RuleFor(command => command.BaseFare)
            .GreaterThanOrEqualTo(0);
        RuleFor(command => command.TotalDistanceKm)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.TotalDistanceKm.HasValue);
        RuleFor(command => command.EstimatedDurationMinutes)
            .GreaterThanOrEqualTo(0)
            .When(command => command.EstimatedDurationMinutes.HasValue);
    }
}
