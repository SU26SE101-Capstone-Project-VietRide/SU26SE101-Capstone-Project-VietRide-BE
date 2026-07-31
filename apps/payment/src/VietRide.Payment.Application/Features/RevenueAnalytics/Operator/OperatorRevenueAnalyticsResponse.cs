namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueAnalyticsResponse(
    OperatorRevenueAnalyticsPeriod Period,
    OperatorRevenueSummary Summary,
    IReadOnlyList<OperatorRevenueMonthItem> Monthly,
    IReadOnlyList<OperatorRoutePerformanceItem> RoutePerformance);
