namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public static class RevenueComparisonFactory
{
    public static RevenueComparison Create(long current, long previous)
    {
        var trend = current > previous
            ? "UP"
            : current < previous
                ? "DOWN"
                : "FLAT";
        if (previous == 0)
        {
            return new RevenueComparison(current, previous, 0m, trend);
        }

        var percent = decimal.Round(
            ((decimal)current - previous) / Math.Abs((decimal)previous) * 100m,
            2,
            MidpointRounding.AwayFromZero);
        return new RevenueComparison(current, previous, percent, trend);
    }
}
