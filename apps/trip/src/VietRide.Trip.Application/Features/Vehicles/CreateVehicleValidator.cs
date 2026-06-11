using FluentValidation;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class CreateVehicleValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.VehicleTypeId).NotEmpty();
        RuleFor(command => command.LicensePlate).NotEmpty().MaximumLength(20);
        RuleFor(command => command.SeatLayoutJson).NotNull();
        RuleFor(command => command.TotalSeats).GreaterThan(0);
        RuleFor(command => command.MaxCargoWeightKg).GreaterThanOrEqualTo(0).When(command => command.MaxCargoWeightKg.HasValue);
        RuleFor(command => command.MaxCargoVolumeM3).GreaterThanOrEqualTo(0).When(command => command.MaxCargoVolumeM3.HasValue);
    }
}
