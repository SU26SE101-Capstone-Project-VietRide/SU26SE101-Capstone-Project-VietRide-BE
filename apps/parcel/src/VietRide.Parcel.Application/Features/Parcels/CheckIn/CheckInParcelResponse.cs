namespace VietRide.Parcel.Application.Features.Parcels.CheckIn;

public sealed record CheckInParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    DateTimeOffset CheckedInAt,
    DateTimeOffset LatestCheckInAt);
