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
        RuleFor(command => command.Code)
            .Matches("^[A-Za-z0-9][A-Za-z0-9-]{1,19}$")
            .When(command => command.Code is not null)
            .WithMessage("Route code must contain 2 to 20 uppercase letters, digits, or hyphens after normalization.");
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
