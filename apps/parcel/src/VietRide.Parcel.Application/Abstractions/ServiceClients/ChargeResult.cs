namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record ChargeResult(
    Guid PaymentId,
    string Status,
    string? PaymentRedirectUrl);
