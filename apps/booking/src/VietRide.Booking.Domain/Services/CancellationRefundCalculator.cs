using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Domain.Services;

public static class CancellationRefundCalculator
{
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
