namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ReweighParcelRequest(
    decimal ActualWeightKg,
    string ActualSizeCategory,
    string PaymentMethod);
