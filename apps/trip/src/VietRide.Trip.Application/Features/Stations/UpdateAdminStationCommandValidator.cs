using FluentValidation;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class UpdateAdminStationCommandValidator : AbstractValidator<UpdateAdminStationCommand>
{
    public UpdateAdminStationCommandValidator()
    {
        RuleFor(command => command.StationId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command)
            .Must(HasAtLeastOneUpdate)
            .WithName("request")
            .WithMessage("At least one Station field must be supplied.");
        RuleFor(command => command)
            .Must(command => command.Latitude.HasValue == command.Longitude.HasValue)
            .WithName("coordinates")
            .WithMessage("Latitude and longitude must be supplied together.");
        RuleFor(command => command.Latitude)
            .InclusiveBetween(-90m, 90m)
            .When(command => command.Latitude.HasValue);
        RuleFor(command => command.Longitude)
            .InclusiveBetween(-180m, 180m)
            .When(command => command.Longitude.HasValue);
        RuleFor(command => command.Name)
            .NotEmpty()
            .When(command => command.Name is not null);
        RuleFor(command => command.City)
            .NotEmpty()
            .When(command => command.City is not null);
        RuleFor(command => command.Province)
            .NotEmpty()
            .When(command => command.Province is not null);
        RuleFor(command => command.LocationId)
            .NotEqual(Guid.Empty)
            .When(command => command.LocationId.HasValue);
    }

    private static bool HasAtLeastOneUpdate(UpdateAdminStationCommand command)
        => command.Name is not null
            || command.AddressStreet is not null
            || command.LocationId.HasValue
            || command.City is not null
            || command.Province is not null
            || command.Latitude.HasValue
            || command.Longitude.HasValue
            || command.ContactPhone is not null
            || command.ContactEmail is not null
            || command.OperatingHours is not null
            || command.Facilities is not null
            || command.SupportsShuttle.HasValue
            || command.IsActive.HasValue;
}
