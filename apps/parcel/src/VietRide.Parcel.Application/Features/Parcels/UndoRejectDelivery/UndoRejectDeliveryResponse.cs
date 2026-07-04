namespace VietRide.Parcel.Application.Features.Parcels.UndoRejectDelivery;

public sealed record UndoRejectDeliveryResponse(Guid ParcelId, string Status, DateTimeOffset UndoneAt);
