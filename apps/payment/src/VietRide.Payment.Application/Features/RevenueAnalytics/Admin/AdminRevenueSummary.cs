using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record AdminRevenueSummary(
    AdminRevenueComparisons Revenue,
    AdminSettlementComparisons Settlement);

public sealed record AdminRevenueComparisons(
    RevenueComparison TotalProjectRevenueVnd,
    RevenueComparison NetTransportRevenueVnd,
    RevenueComparison NetTicketRevenueVnd,
    RevenueComparison NetParcelRevenueVnd,
    RevenueComparison SubscriptionRevenueVnd);

public sealed record AdminSettlementComparisons(RevenueComparison PaidToOperatorsVnd);
