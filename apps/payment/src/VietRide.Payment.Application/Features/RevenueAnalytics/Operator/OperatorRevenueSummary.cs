using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueSummary(
    RevenueComparison TotalRevenueVnd,
    RevenueComparison TicketRevenueVnd,
    RevenueComparison ParcelRevenueVnd,
    RevenueComparison AverageRevenuePerTripVnd);
