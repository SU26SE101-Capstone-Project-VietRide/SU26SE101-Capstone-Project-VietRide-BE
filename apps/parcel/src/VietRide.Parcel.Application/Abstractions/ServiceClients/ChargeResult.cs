namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record ChargeResult(
    Guid PaymentId,
    string Status,
    string? PaymentRedirectUrl,
    DateTimeOffset? DueAt = null,
    string? PaymentReturnMode = null,
    VnPaySdkMetadata? VnPaySdk = null);
