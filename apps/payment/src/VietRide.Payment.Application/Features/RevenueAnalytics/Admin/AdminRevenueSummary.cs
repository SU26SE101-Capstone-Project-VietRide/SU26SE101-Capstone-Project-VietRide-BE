using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record AdminRevenueSummary(
    RevenueComparison GrossRevenueVnd,
    RevenueComparison PlatformRevenueVnd,
    RevenueComparison PaidToOperatorsVnd);
