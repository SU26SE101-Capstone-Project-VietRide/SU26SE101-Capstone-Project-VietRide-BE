namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record UpdateParcelStatusRequest(string TargetStatus, string? Reason);
