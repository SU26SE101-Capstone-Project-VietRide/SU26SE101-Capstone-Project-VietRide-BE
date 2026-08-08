using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueSummary(
    RevenueComparison NetRevenueVnd,
    RevenueComparison NetTicketRevenueVnd,
    RevenueComparison NetParcelRevenueVnd,
    RevenueComparison AverageNetRevenuePerTripVnd);
