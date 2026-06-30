namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record MarkParcelLoadedRequest(Guid TripId, string ParcelCode, Guid? ConfirmedByUserId);
