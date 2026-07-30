namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record AdminRevenueMonthReadModel(
    DateOnly Month,
    long PlatformRevenueVnd,
    long PaidToOperatorsVnd);
