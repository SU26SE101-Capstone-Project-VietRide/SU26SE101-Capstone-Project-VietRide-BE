namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record UnloadParcelRequest(
    string ParcelCode,
    ParcelActualLocationRequest ActualLocation,
    IReadOnlyCollection<string>? PhotoUrls);

public sealed record ParcelActualLocationRequest(string Kind, Guid Id);
