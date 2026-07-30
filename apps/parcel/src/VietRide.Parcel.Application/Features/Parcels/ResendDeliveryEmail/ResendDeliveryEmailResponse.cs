namespace VietRide.Parcel.Application.Features.Parcels.ResendDeliveryEmail;

public sealed record ResendDeliveryEmailResponse(
    Guid ParcelId,
    string Status,
    DateTimeOffset ExpiresAt);
