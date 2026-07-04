using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record GetTripTrackingAuthorizationQuery(
    Guid TripId,
    Guid? UserId,
    string? Role,
    Guid? OperatorId) : IRequest<TrackingAuthorizationResponse>;
