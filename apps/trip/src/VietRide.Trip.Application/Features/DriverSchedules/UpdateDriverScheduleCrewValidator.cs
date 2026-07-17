using FluentValidation;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class UpdateDriverScheduleCrewValidator : AbstractValidator<UpdateDriverScheduleCrewCommand>
{
    public UpdateDriverScheduleCrewValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.DriverScheduleId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.DriverUserId).NotEmpty();
        RuleFor(command => command.AssistantUserId)
            .NotEqual(Guid.Empty)
            .When(command => command.AssistantUserId.HasValue);
    }
}
