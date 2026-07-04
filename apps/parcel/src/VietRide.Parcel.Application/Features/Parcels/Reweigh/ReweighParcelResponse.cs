namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed record ReweighParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    long AdditionalAmount,
    string? PaymentRedirectUrl);
