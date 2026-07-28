namespace VietRide.Parcel.Application.Features.Parcels.DepositPayment;

public sealed record ParcelDepositPaymentResponse(
    Guid ParcelId,
    string Status,
    Guid? DepositPaymentId,
    long DepositRequiredVnd,
    long DepositPaidVnd,
    DateTimeOffset? PaymentDueAt,
    string? PaymentRedirectUrl);
