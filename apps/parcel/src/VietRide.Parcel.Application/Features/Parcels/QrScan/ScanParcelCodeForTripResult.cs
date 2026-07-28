namespace VietRide.Parcel.Application.Features.Parcels.QrScan;

public sealed record ScanParcelCodeForTripResult(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    Guid TripId,
    string? RecipientName,
    string SizeCategory,
    string? PhotoUrl);
