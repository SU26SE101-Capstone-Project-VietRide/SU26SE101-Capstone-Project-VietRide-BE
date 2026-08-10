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
        RuleFor(command => command.LocationId)
            .Must(locationId => locationId != Guid.Empty)
            .WithMessage("Location id must be valid.")
            .When(command => command.LocationId.HasValue);
        RuleFor(command => command.LocationCode)
            .Length(5)
            .MaximumLength(20)
            .When(command => !string.IsNullOrWhiteSpace(command.LocationCode));
        RuleFor(command => command)
            .Must(command => command.LocationId.HasValue ^ !string.IsNullOrWhiteSpace(command.LocationCode))
            .WithName(nameof(CreateStopCommand.LocationId))
            .WithMessage("Provide exactly one of locationId or locationCode.");
        RuleFor(command => command.Latitude)
            .NotNull()
            .InclusiveBetween(-90m, 90m);
        RuleFor(command => command.Longitude)
            .NotNull()
            .InclusiveBetween(-180m, 180m);
    }
}
