namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record CheckInParcelRequest(
    Guid TripId,
    string ParcelCode,
    IReadOnlyCollection<string>? PhotoUrls);
