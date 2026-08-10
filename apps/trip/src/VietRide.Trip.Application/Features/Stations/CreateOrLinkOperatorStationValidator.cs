using FluentValidation;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class CreateOrLinkOperatorStationValidator : AbstractValidator<CreateOrLinkOperatorStationCommand>
{
    public CreateOrLinkOperatorStationValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty();

        When(command => command.StationId.HasValue, () =>
        {
            RuleFor(command => command.StationId)
                .Must(stationId => stationId != Guid.Empty)
                .WithMessage("Station id must be valid.");

            RuleFor(command => command.DisplayNameOverride)
                .MaximumLength(255)
                .When(command => !string.IsNullOrWhiteSpace(command.DisplayNameOverride));
            RuleFor(command => command.CounterLocation)
                .MaximumLength(255)
                .When(command => !string.IsNullOrWhiteSpace(command.CounterLocation));
            RuleFor(command => command.OperatorStationContactPhone)
                .MaximumLength(20)
                .When(command => !string.IsNullOrWhiteSpace(command.OperatorStationContactPhone));
            RuleFor(command => command.Instructions)
                .MaximumLength(4000)
                .When(command => !string.IsNullOrWhiteSpace(command.Instructions));
        });

        When(command => !command.StationId.HasValue, () =>
        {
            RuleFor(command => command.Name)
                .NotEmpty()
                .MaximumLength(255);
            RuleFor(command => command.City)
                .MaximumLength(100);
            RuleFor(command => command.Ward)
                .MaximumLength(100);
            RuleFor(command => command)
                .Must(command => command.LocationId.HasValue ^ !string.IsNullOrWhiteSpace(command.LocationCode))
                .WithName(nameof(CreateOrLinkOperatorStationCommand.LocationId))
                .WithMessage("Provide exactly one of locationId or locationCode.");
            RuleFor(command => command.LocationId)
                .Must(locationId => locationId != Guid.Empty)
                .WithMessage("Location id must be valid.")
                .When(command => command.LocationId.HasValue);
            RuleFor(command => command.LocationCode)
                .Length(5)
                .MaximumLength(20)
                .When(command => !string.IsNullOrWhiteSpace(command.LocationCode));
            RuleFor(command => command.AddressStreet)
                .MaximumLength(500)
                .When(command => !string.IsNullOrWhiteSpace(command.AddressStreet));
            RuleFor(command => command.StationContactPhone)
                .MaximumLength(20)
                .When(command => !string.IsNullOrWhiteSpace(command.StationContactPhone));
            RuleFor(command => command.ContactEmail)
                .MaximumLength(255)
                .EmailAddress()
                .When(command => !string.IsNullOrWhiteSpace(command.ContactEmail));
            RuleFor(command => command.OperatingHours)
                .MaximumLength(4000)
                .When(command => !string.IsNullOrWhiteSpace(command.OperatingHours));
            RuleFor(command => command.Facilities)
                .MaximumLength(4000)
                .When(command => !string.IsNullOrWhiteSpace(command.Facilities));
            RuleFor(command => command.DisplayNameOverride)
                .MaximumLength(255)
                .When(command => !string.IsNullOrWhiteSpace(command.DisplayNameOverride));
            RuleFor(command => command.CounterLocation)
                .MaximumLength(255)
                .When(command => !string.IsNullOrWhiteSpace(command.CounterLocation));
            RuleFor(command => command.OperatorStationContactPhone)
                .MaximumLength(20)
                .When(command => !string.IsNullOrWhiteSpace(command.OperatorStationContactPhone));
            RuleFor(command => command.Instructions)
                .MaximumLength(4000)
                .When(command => !string.IsNullOrWhiteSpace(command.Instructions));
            RuleFor(command => command.Latitude)
                .NotNull();
            RuleFor(command => command.Longitude)
                .NotNull();
        });
    }
}
