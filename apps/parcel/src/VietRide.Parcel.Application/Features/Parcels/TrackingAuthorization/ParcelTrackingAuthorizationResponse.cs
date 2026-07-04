namespace VietRide.Parcel.Application.Features.Parcels.TrackingAuthorization;

public sealed record ParcelTrackingAuthorizationResponse(
    bool Allowed,
    string? Scope = null,
    string? Error = null);
