namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record AdminRevenueMonthItem(
    string Month,
    long GrossRevenueVnd,
    long PaidToOperatorsVnd,
    long PlatformRevenueVnd);
