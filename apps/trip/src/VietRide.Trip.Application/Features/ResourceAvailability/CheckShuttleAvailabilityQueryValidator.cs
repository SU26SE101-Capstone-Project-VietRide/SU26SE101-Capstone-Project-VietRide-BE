using FluentValidation;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public sealed class CheckShuttleAvailabilityQueryValidator : AbstractValidator<CheckShuttleAvailabilityQuery>
{
    public CheckShuttleAvailabilityQueryValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.MainTripId).NotEmpty();
        RuleFor(x => x.Direction)
            .Must(direction => direction is "INBOUND_TO_STATION" or "OUTBOUND_FROM_STATION");
        RuleFor(x => x.DriverUserId).NotEmpty();
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.ScheduledEndTime).GreaterThan(x => x.ScheduledDepartureTime);
        RuleFor(x => x.OrderedBookingIds)
            .NotEmpty()
            .Must(ids => ids.All(id => id != Guid.Empty))
            .Must(ids => ids.Distinct().Count() == ids.Count);
    }
}
