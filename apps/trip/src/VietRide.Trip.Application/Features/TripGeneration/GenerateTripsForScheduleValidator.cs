using FluentValidation;

namespace VietRide.Trip.Application.Features.TripGeneration;

public sealed class GenerateTripsForScheduleValidator : AbstractValidator<GenerateTripsForScheduleCommand>
{
    public GenerateTripsForScheduleValidator()
    {
        RuleFor(command => command.DriverScheduleId).NotEmpty().When(command => command.DriverScheduleId.HasValue);
    }
}
