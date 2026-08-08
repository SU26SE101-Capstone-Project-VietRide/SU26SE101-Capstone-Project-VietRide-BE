namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record RevenueComparison(
    long CurrentValue,
    long PreviousValue,
    decimal? ChangePercent,
    string Trend);
