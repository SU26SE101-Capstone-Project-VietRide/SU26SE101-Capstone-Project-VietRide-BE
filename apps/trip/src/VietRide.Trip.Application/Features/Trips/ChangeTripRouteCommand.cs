using MediatR;
using VietRide.Shared.Application.Behaviors;
using VietRide.Trip.Application.Events;

namespace VietRide.Trip.Application.Features.Trips;

[SkipTransaction]
public sealed record ChangeTripRouteCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    Guid AlternativeRouteId) : IRequest<ChangeTripRouteResponse>;

public sealed record ChangeTripRouteResponse(
    Guid TripId,
    string Status,
    Guid AlternativeRouteId,
    IReadOnlyList<TripRouteChangedAffectedBooking> AffectedBookings);
