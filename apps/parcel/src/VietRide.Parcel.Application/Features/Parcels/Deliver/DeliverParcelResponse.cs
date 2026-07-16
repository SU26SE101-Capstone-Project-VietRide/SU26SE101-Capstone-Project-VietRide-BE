namespace VietRide.Parcel.Application.Features.Parcels.Deliver;

public sealed record DeliverParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    DateTimeOffset DeliveredPendingConfirmAt);
