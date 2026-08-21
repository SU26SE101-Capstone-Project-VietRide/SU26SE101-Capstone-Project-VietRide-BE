namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantParcelPaymentStateResponse(
    long DepositRequiredVnd,
    long DepositPaidVnd,
    long BalanceRequiredVnd,
    long BalancePaidVnd,
    DateTimeOffset? FinalPaymentDeadline,
    bool IsFullyPaid);
