using MediatR;

namespace VietRide.Trip.Application.Features.Trips;

public sealed record ChangeTripRouteCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    Guid AlternativeRouteId) : IRequest<ChangeTripRouteResponse>;

public sealed record ChangeTripRouteResponse(
    Guid TripId,
    string Status,
    Guid AlternativeRouteId,
    IReadOnlyList<Guid> AffectedBookingIds);
