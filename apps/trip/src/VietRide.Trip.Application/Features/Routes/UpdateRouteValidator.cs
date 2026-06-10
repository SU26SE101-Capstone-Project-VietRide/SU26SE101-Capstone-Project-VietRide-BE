using FluentValidation;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class UpdateRouteValidator : AbstractValidator<UpdateRouteCommand>
{
    public UpdateRouteValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();
        RuleFor(command => command.RouteId)
            .NotEmpty();
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(255)
            .When(command => command.Name is not null);
        RuleFor(command => command.ReturnRouteId)
            .NotEmpty()
            .When(command => command.ReturnRouteId.HasValue);
        RuleFor(command => command.BaseFare)
            .GreaterThanOrEqualTo(0)
            .When(command => command.BaseFare.HasValue);
        RuleFor(command => command.TotalDistanceKm)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.TotalDistanceKm.HasValue);
        RuleFor(command => command.EstimatedDurationMinutes)
            .GreaterThanOrEqualTo(0)
            .When(command => command.EstimatedDurationMinutes.HasValue);
    }
}
