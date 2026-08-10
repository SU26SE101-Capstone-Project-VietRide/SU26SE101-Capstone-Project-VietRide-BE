namespace VietRide.Payment.Application.Features.TopUps.CreateTopUp;

using System.Text.Json.Serialization;
using VietRide.Payment.Application.Abstractions.ExternalClients;

public sealed record CreateTopUpResult(
    Guid TopUpRequestId,
    string Status,
    string PaymentRedirectUrl,
    string PaymentReturnMode,
    [property: JsonPropertyName("vnpaySdk")] VnPaySdkConfiguration VnPaySdk);
