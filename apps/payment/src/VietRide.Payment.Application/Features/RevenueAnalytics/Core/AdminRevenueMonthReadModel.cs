namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record AdminRevenueMonthReadModel(
    DateOnly Month,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long SubscriptionRevenueVnd,
    long PaidToOperatorsVnd);
