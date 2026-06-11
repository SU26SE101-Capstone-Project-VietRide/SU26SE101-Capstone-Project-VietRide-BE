using FluentValidation;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class UpdateVehicleValidator : AbstractValidator<UpdateVehicleCommand>
{
    public UpdateVehicleValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.VehicleId).NotEmpty();
        RuleFor(command => command.VehicleTypeId).NotEmpty().When(command => command.VehicleTypeId.HasValue);
        RuleFor(command => command.LicensePlate).NotEmpty().MaximumLength(20).When(command => command.LicensePlate is not null);
        RuleFor(command => command.TotalSeats).GreaterThan(0).When(command => command.TotalSeats.HasValue);
        RuleFor(command => command.MaxCargoWeightKg).GreaterThanOrEqualTo(0).When(command => command.MaxCargoWeightKg.HasValue);
        RuleFor(command => command.MaxCargoVolumeM3).GreaterThanOrEqualTo(0).When(command => command.MaxCargoVolumeM3.HasValue);
    }
}
