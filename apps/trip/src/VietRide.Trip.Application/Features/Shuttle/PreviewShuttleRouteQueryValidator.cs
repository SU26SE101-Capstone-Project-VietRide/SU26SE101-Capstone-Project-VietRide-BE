using FluentValidation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class PreviewShuttleRouteQueryValidator : AbstractValidator<PreviewShuttleRouteQuery>
{
    public PreviewShuttleRouteQueryValidator()
    {
        RuleFor(request => request.OperatorId).NotEmpty();
        RuleFor(request => request.MainTripId).NotEmpty();
        RuleFor(request => request.Direction)
            .NotEmpty()
            .Must(direction => direction is ShuttleTrip.InboundDirection or ShuttleTrip.OutboundDirection)
            .WithMessage("direction must be INBOUND_TO_STATION or OUTBOUND_FROM_STATION.");
        RuleFor(request => request.ScheduledDepartureTime).NotEmpty();
        RuleFor(request => request.OrderedBookingIds)
            .NotEmpty()
            .Must(ids => ids is not null && ids.All(id => id != Guid.Empty))
            .WithMessage("orderedBookingIds cannot contain an empty identifier.")
            .Must(ids => ids is not null && ids.Distinct().Count() == ids.Count)
            .WithMessage("orderedBookingIds must contain distinct identifiers.");
    }
}
