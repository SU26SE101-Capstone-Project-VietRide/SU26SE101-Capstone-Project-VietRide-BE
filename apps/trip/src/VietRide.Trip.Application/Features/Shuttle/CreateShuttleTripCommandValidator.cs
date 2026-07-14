using FluentValidation;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class CreateShuttleTripCommandValidator : AbstractValidator<CreateShuttleTripCommand>
{
    public CreateShuttleTripCommandValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.MainTripId).NotEmpty();
        RuleFor(x => x.DriverUserId).NotEmpty();
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.ScheduledDepartureTime).NotEmpty();
        RuleFor(x => x.ScheduledEndTime)
            .GreaterThan(x => x.ScheduledDepartureTime)
            .WithMessage("scheduledEndTime must be after scheduledDepartureTime.");
        RuleFor(x => x.OrderedBookingIds)
            .NotEmpty()
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("orderedBookingIds cannot contain an empty identifier.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("orderedBookingIds must contain distinct identifiers.");
        RuleFor(x => x.Notes).MaximumLength(1_000);
    }
}
