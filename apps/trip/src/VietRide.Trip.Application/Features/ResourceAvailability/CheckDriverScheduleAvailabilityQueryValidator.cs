using FluentValidation;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public sealed class CheckDriverScheduleAvailabilityQueryValidator
    : AbstractValidator<CheckDriverScheduleAvailabilityQuery>
{
    public CheckDriverScheduleAvailabilityQueryValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.DriverUserId).NotEmpty();
        RuleFor(x => x.DayOfWeek)
            .NotEmpty()
            .Must(days => days.All(day => day is >= 1 and <= 7))
            .WithMessage("dayOfWeek values must be between 1 and 7.")
            .Must(days => days.Distinct().Count() == days.Count)
            .WithMessage("dayOfWeek values must be distinct.");
        RuleFor(x => x.ValidUntil)
            .GreaterThanOrEqualTo(x => x.ValidFrom)
            .When(x => x.ValidUntil.HasValue);
        RuleFor(x => x.AssistantUserId)
            .NotEqual(x => x.DriverUserId)
            .When(x => x.AssistantUserId.HasValue);
    }
}
