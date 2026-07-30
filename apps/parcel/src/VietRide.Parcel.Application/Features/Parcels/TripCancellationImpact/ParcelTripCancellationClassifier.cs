using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;

public enum ParcelTripCancellationDisposition
{
    None,
    CancelAndRefund,
    PendingOperatorAction,
}

public sealed record ParcelTripCancellationClassification(
    ParcelTripCancellationDisposition Disposition,
    ParcelStatus? TargetStatus,
    long RefundAmountVnd);

public static class ParcelTripCancellationClassifier
{
    public static ParcelTripCancellationClassification Classify(
        TripCancellationParcelCandidate candidate)
        => Classify(
            candidate.Status,
            candidate.DepositPaidVnd,
            candidate.BalancePaidVnd,
            candidate.RefundedAmountVnd);

    public static ParcelTripCancellationClassification Classify(
        ParcelStatus status,
        long depositPaidVnd,
        long balancePaidVnd,
        long refundedAmountVnd)
    {
        if (IsPreLoad(status))
        {
            return new ParcelTripCancellationClassification(
                ParcelTripCancellationDisposition.CancelAndRefund,
                ParcelStatus.CANCELLED,
                CalculateOutstandingCollected(
                    depositPaidVnd,
                    balancePaidVnd,
                    refundedAmountVnd));
        }

        return status is ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT
            ? new ParcelTripCancellationClassification(
                ParcelTripCancellationDisposition.PendingOperatorAction,
                ParcelStatus.PENDING_OPERATOR_ACTION,
                0)
            : new ParcelTripCancellationClassification(
                ParcelTripCancellationDisposition.None,
                null,
                0);
    }

    public static bool IsPreLoad(ParcelStatus status)
        => status is ParcelStatus.PENDING_OPERATOR_REVIEW
            or ParcelStatus.PENDING_PAYMENT
            or ParcelStatus.PENDING
            or ParcelStatus.PENDING_ADDITIONAL_PAYMENT
            or ParcelStatus.RESERVED
            or ParcelStatus.CHECKED_IN
            or ParcelStatus.PENDING_FINAL_PAYMENT
            or ParcelStatus.READY_TO_LOAD;

    public static long CalculateOutstandingCollected(
        long depositPaidVnd,
        long balancePaidVnd,
        long refundedAmountVnd)
    {
        var collected = checked(depositPaidVnd + balancePaidVnd);
        return Math.Max(checked(collected - refundedAmountVnd), 0);
    }
}
