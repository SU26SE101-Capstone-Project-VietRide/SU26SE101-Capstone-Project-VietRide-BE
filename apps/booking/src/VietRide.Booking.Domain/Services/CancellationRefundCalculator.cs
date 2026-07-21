using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Domain.Services;

public static class CancellationRefundCalculator
{
    public static Money CalculateExplicitPercentRefund(Money refundBasis, int refundPercent)
    {
        if (refundBasis.Amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refundBasis), "Refund basis cannot be negative.");
        }

        if (refundPercent is not (50 or 100))
        {
            throw new ArgumentOutOfRangeException(nameof(refundPercent), "Schedule-change refund must be 50% or 100%.");
        }

        var amount = Math.Round(
            refundBasis.Amount * (refundPercent / 100m),
            0,
            MidpointRounding.AwayFromZero);
        return Money.FromRaw((long)amount);
    }

    public static Money CalculateRefundAmount(
        Money paidAmount,
        decimal hoursToDeparture,
        IReadOnlyCollection<CancellationPolicyTier>? policy,
        bool refundOverride)
    {
        if (paidAmount.Amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paidAmount), "Paid amount cannot be negative.");
        }

        var feePercent = GetFeePercent(hoursToDeparture, policy, refundOverride);
        var refundAmount = Math.Round(
            paidAmount.Amount * (100 - feePercent) / 100,
            0,
            MidpointRounding.AwayFromZero);

        return Money.FromRaw((long)refundAmount);
    }

    private static decimal GetFeePercent(
        decimal hoursToDeparture,
        IReadOnlyCollection<CancellationPolicyTier>? policy,
        bool refundOverride)
    {
        if (refundOverride || policy is null || policy.Count == 0)
        {
            return 0;
        }

        foreach (var tier in policy)
        {
            if (tier.HoursBeforeDeparture >= hoursToDeparture)
            {
                return tier.FeePercent;
            }
        }

        return 0;
    }
}
