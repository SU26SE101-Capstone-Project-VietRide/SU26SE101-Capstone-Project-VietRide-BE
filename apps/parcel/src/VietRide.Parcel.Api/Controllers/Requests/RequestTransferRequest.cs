namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record RequestTransferRequest(Guid TargetTripId, string? Reason);
