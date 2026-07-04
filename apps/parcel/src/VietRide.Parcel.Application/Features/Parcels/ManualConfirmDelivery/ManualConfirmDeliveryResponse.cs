namespace VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;

public sealed record ManualConfirmDeliveryResponse(Guid ParcelId, string Status, DateTimeOffset ConfirmedAt);
