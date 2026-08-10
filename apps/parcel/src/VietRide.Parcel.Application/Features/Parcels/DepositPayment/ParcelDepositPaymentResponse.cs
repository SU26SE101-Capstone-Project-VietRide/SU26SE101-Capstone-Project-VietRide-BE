namespace VietRide.Parcel.Application.Features.Parcels.DepositPayment;

using System.Text.Json.Serialization;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record ParcelDepositPaymentResponse(
    Guid ParcelId,
    string Status,
    Guid? DepositPaymentId,
    long DepositRequiredVnd,
    long DepositPaidVnd,
    DateTimeOffset? PaymentDueAt,
    string? PaymentRedirectUrl,
    string? PaymentReturnMode = null,
    [property: JsonPropertyName("vnpaySdk")] VnPaySdkMetadata? VnPaySdk = null);
