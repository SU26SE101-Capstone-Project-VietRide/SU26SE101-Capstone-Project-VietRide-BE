namespace VietRide.Parcel.Application.Features.Parcels.ConfirmDelivery;

public sealed record ConfirmDeliveryResponse(Guid ParcelId, string Status, DateTimeOffset ConfirmedAt);
