namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ReweighParcelRequest(
    decimal ActualLengthCm,
    decimal ActualWidthCm,
    decimal ActualHeightCm,
    decimal ActualWeightKg,
    string ActualSizeCategory,
    string PaymentMethod);
