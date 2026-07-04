namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ReviewParcelRequest(
    string Decision,
    long? DepositAmount,
    string? Reason,
    string? PaymentMethod);
