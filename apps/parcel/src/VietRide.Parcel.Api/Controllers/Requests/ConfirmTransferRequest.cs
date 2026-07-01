namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ConfirmTransferRequest(Guid TargetTripId, string ParcelCode, Guid ConfirmedByUserId);
