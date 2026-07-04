namespace VietRide.Parcel.Application.Features.Parcels.RejectDelivery;

public sealed record RejectDeliveryResponse(Guid ParcelId, string Status, DateTimeOffset RejectedAt, DateTimeOffset CanUndoUntil);
