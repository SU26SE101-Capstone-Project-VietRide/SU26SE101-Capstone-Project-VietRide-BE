using FluentValidation;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class UpdateStopValidator : AbstractValidator<UpdateStopCommand>
{
    public UpdateStopValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();
        RuleFor(command => command.StopId)
            .NotEmpty();
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(255)
            .When(command => command.Name is not null);
        RuleFor(command => command.Description)
            .MaximumLength(4000)
            .When(command => !string.IsNullOrWhiteSpace(command.Description));
        RuleFor(command => command.Address)
            .MaximumLength(500)
            .When(command => !string.IsNullOrWhiteSpace(command.Address));
        RuleFor(command => command.GooglePlaceId)
            .MaximumLength(255)
            .When(command => !string.IsNullOrWhiteSpace(command.GooglePlaceId));
        RuleFor(command => command.Latitude)
            .InclusiveBetween(-90m, 90m)
            .When(command => command.Latitude.HasValue);
        RuleFor(command => command.Longitude)
            .InclusiveBetween(-180m, 180m)
            .When(command => command.Longitude.HasValue);
    }
}
