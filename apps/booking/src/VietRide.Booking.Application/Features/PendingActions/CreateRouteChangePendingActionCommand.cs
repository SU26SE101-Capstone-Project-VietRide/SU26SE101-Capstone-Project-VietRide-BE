using MediatR;

namespace VietRide.Booking.Application.Features.PendingActions;

public sealed record CreateRouteChangePendingActionCommand(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TripId,
    Guid OperatorId,
    string TripStatus,
    Guid AlternativeRouteId,
    IReadOnlyList<RouteChangeAffectedBooking> AffectedBookings) : IRequest<int>;
