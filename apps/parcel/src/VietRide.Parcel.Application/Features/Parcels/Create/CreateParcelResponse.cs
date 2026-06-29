namespace VietRide.Parcel.Application.Features.Parcels.Create;

public sealed record CreateParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    long TotalAmount,
    string? PaymentRedirectUrl);
