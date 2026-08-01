namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record AdminRevenueAnalyticsResponse(
    AdminRevenuePeriod Period,
    AdminRevenueSummary Summary,
    IReadOnlyList<AdminRevenueMonthItem> Monthly,
    IReadOnlyList<AdminTopOperatorItem> TopOperators);
