using FluentValidation;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class ReassignShuttleTripCommandValidator : AbstractValidator<ReassignShuttleTripCommand>
{
    public ReassignShuttleTripCommandValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.ShuttleTripId).NotEmpty();
        RuleFor(command => command)
            .Must(command => command.DriverUserId.HasValue || command.VehicleId.HasValue)
            .WithMessage("At least one of driverUserId or vehicleId is required.");
        RuleFor(command => command.DriverUserId)
            .NotEqual(Guid.Empty)
            .When(command => command.DriverUserId.HasValue);
        RuleFor(command => command.VehicleId)
            .NotEqual(Guid.Empty)
            .When(command => command.VehicleId.HasValue);
        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}
