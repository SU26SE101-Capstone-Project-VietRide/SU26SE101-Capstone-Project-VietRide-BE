namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record AdminRevenueMonthItem(
    string Month,
    AdminRevenueMonthValues Revenue,
    AdminSettlementMonthValues Settlement);

public sealed record AdminRevenueMonthValues(
    long TotalProjectRevenueVnd,
    long NetTransportRevenueVnd,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long SubscriptionRevenueVnd);

public sealed record AdminSettlementMonthValues(long PaidToOperatorsVnd);
