using FluentValidation;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class CreateDriverScheduleValidator : AbstractValidator<CreateDriverScheduleCommand>
{
    public CreateDriverScheduleValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.RouteId).NotEmpty();
        RuleFor(command => command.DriverUserId).NotEmpty();
        RuleFor(command => command.VehicleId).NotEqual(Guid.Empty).When(command => command.VehicleId.HasValue);
        RuleFor(command => command.AssistantUserId).NotEqual(Guid.Empty).When(command => command.AssistantUserId.HasValue);
        RuleFor(command => command.DayOfWeek).NotEmpty();
        RuleForEach(command => command.DayOfWeek).InclusiveBetween(1, 7);
        RuleFor(command => command.ValidUntil)
            .GreaterThanOrEqualTo(command => command.ValidFrom)
            .When(command => command.ValidUntil.HasValue);
    }
}
