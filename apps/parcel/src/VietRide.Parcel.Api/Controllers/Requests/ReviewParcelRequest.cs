namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ReviewParcelRequest(
    string Decision,
    string? Reason);
