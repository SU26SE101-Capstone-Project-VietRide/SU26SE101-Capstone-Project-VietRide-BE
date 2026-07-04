namespace VietRide.Parcel.Application.Features.Parcels.Review;

public sealed record ReviewParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    long? DepositAmount,
    string? PaymentRedirectUrl);
