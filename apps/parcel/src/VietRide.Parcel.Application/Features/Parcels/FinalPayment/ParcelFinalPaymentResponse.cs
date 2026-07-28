namespace VietRide.Parcel.Application.Features.Parcels.FinalPayment;

public sealed record ParcelFinalPaymentResponse(
    Guid ParcelId,
    string Status,
    Guid? BalancePaymentId,
    long BalanceRequiredVnd,
    long BalancePaidVnd,
    DateTimeOffset FinalPaymentDeadline,
    string? PaymentRedirectUrl);
