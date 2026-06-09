using FluentValidation;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class CreateStopValidator : AbstractValidator<CreateStopCommand>
{
    public CreateStopValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(255);
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
            .NotNull()
            .InclusiveBetween(-90m, 90m);
        RuleFor(command => command.Longitude)
            .NotNull()
            .InclusiveBetween(-180m, 180m);
    }
}
