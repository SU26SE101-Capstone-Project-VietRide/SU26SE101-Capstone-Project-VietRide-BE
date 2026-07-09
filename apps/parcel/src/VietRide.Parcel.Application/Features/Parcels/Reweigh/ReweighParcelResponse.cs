namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed record ReweighParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    decimal ActualChargeableWeightKg,
    long TotalPriceVnd,
    long AdditionalAmount,
    long RefundAmount,
    string? PaymentRedirectUrl);
