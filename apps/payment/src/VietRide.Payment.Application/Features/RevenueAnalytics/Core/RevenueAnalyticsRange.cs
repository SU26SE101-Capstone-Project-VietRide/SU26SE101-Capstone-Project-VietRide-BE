namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record RevenueAnalyticsRange(
    DateOnly From,
    DateOnly To,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset PreviousFromUtc,
    DateTimeOffset PreviousToUtc);
