namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record OperatorRevenuePeriod(
    string Month,
    DateOnly From,
    DateOnly To,
    DateTimeOffset CurrentFromUtc,
    DateTimeOffset CurrentToUtc,
    DateTimeOffset PreviousFromUtc,
    DateTimeOffset PreviousToUtc,
    DateTimeOffset TwelveMonthFromUtc,
    IReadOnlyList<string> Months);
