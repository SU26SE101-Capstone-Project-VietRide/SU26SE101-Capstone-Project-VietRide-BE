namespace VietRide.Parcel.Infrastructure.Http;

internal sealed record TripTrackingAuthorizationResponse(
    bool Allowed,
    string? Scope,
    string? Error);
