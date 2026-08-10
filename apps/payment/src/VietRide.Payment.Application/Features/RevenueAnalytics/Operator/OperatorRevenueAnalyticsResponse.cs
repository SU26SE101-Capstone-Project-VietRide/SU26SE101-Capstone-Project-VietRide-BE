using System.Text.Json.Serialization;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueAnalyticsResponse(
    OperatorRevenueAnalyticsPeriod Period,
    OperatorRevenueSummary Summary,
    IReadOnlyList<OperatorRevenueMonthItem> Monthly,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<OperatorRoutePerformanceItem>? RoutePerformance,
    DateTimeOffset GeneratedAt);
