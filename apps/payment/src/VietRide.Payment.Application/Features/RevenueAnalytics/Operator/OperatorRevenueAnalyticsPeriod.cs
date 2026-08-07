namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueAnalyticsPeriod(
    string? Month,
    int? Year,
    string GroupBy,
    DateOnly From,
    DateOnly To,
    string Timezone);
