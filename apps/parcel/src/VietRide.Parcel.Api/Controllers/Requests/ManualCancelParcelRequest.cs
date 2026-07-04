namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ManualCancelParcelRequest(
    string Reason,
    string? RefundChoice);
