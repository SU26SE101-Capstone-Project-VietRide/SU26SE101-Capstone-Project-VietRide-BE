using FluentValidation;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class ActivateDriverScheduleValidator : AbstractValidator<ActivateDriverScheduleCommand>
{
    public ActivateDriverScheduleValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.DriverScheduleId).NotEmpty();
    }
}
