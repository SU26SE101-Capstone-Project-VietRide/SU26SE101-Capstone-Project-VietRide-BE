namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueAnalyticsPeriod(
    string Month,
    DateOnly From,
    DateOnly To,
    string Timezone);
