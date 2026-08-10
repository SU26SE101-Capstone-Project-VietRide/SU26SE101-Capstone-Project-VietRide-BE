namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

using System.Text.Json.Serialization;
using VietRide.Payment.Application.Abstractions.ExternalClients;

public sealed record ChargePaymentResult(
    Guid PaymentId,
    string Status,
    string? PaymentRedirectUrl,
    DateTimeOffset? DueAt = null,
    string? PaymentReturnMode = null,
    [property: JsonPropertyName("vnpaySdk")] VnPaySdkConfiguration? VnPaySdk = null);
