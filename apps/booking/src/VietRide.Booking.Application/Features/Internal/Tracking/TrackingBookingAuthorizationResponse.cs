namespace VietRide.Booking.Application.Features.Internal.Tracking;

public sealed record TrackingBookingAuthorizationResponse(
    bool Allowed,
    string? Scope = null,
    string? Error = null);
