using MediatR;

namespace VietRide.Booking.Application.Features.Internal.Tracking;

public sealed record GetBookingTrackingAuthorizationQuery(
    Guid TripId,
    Guid? UserId,
    string? Role) : IRequest<TrackingBookingAuthorizationResponse>;
