namespace VietRide.Parcel.Application.Features.Parcels.FinalPayment;

using System.Text.Json.Serialization;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record ParcelFinalPaymentResponse(
    Guid ParcelId,
    string Status,
    Guid? BalancePaymentId,
    long BalanceRequiredVnd,
    long BalancePaidVnd,
    DateTimeOffset FinalPaymentDeadline,
    string? PaymentRedirectUrl,
    string? PaymentReturnMode = null,
    [property: JsonPropertyName("vnpaySdk")] VnPaySdkMetadata? VnPaySdk = null);
