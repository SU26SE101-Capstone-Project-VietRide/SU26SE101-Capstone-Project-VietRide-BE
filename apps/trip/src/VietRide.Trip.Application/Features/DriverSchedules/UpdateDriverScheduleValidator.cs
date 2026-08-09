using FluentValidation;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class UpdateDriverScheduleValidator : AbstractValidator<UpdateDriverScheduleCommand>
{
    public UpdateDriverScheduleValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.DriverScheduleId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.ApplyTo)
            .Must(value => value is UpdateDriverScheduleCommand.FutureOnly or UpdateDriverScheduleCommand.AllPending)
            .WithMessage("applyTo must be FUTURE_ONLY or ALL_PENDING.");
        RuleFor(command => command)
            .Must(command => command.DepartureTimeSpecified
                || command.DayOfWeekSpecified
                || command.DriverUserIdSpecified
                || command.AssistantUserIdSpecified
                || command.VehicleIdSpecified
                || command.ValidUntilSpecified
                || command.IsActiveSpecified
                || command.BaseFareSpecified)
            .WithMessage("At least one editable field is required.");

        RuleFor(command => command.DepartureTime)
            .NotNull()
            .When(command => command.DepartureTimeSpecified);
        RuleFor(command => command.DayOfWeek)
            .NotNull()
            .Must(days => days is { Count: > 0 } && days.All(day => day is >= 1 and <= 7))
            .When(command => command.DayOfWeekSpecified)
            .WithMessage("dayOfWeek must contain integers from 1 through 7.");
        RuleFor(command => command.DriverUserId)
            .NotNull()
            .NotEqual(Guid.Empty)
            .When(command => command.DriverUserIdSpecified);
        RuleFor(command => command.AssistantUserId)
            .NotEqual(Guid.Empty)
            .When(command => command.AssistantUserIdSpecified && command.AssistantUserId.HasValue);
        RuleFor(command => command.VehicleId)
            .NotEqual(Guid.Empty)
            .When(command => command.VehicleIdSpecified && command.VehicleId.HasValue);
        RuleFor(command => command.IsActive)
            .NotNull()
            .When(command => command.IsActiveSpecified);

        RuleFor(command => command.BaseFare)
            .GreaterThanOrEqualTo(0)
            .When(command => command.BaseFareSpecified && command.BaseFare.HasValue);

        RuleFor(command => command.BaseFare)
            .Must(_ => false)
            .WithMessage("baseFare can only be changed with applyTo=FUTURE_ONLY.")
            .When(command => command.BaseFareSpecified && command.ApplyTo == UpdateDriverScheduleCommand.AllPending);
    }
}
