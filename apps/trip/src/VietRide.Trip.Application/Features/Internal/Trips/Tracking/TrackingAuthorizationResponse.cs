namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record TrackingAuthorizationResponse(
    bool Allowed,
    string? Scope = null,
    string? Error = null);
