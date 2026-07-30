namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record ParcelDeliveryEmailRequest(
    Guid DeliveryTokenId,
    string ToEmail,
    Guid DeliveryToken,
    string ParcelCode,
    DateTimeOffset ExpiresAt);
