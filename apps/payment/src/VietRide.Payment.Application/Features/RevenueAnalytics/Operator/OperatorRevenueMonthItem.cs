namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueMonthItem(
    string Month,
    long NetRevenueVnd,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    int TripCount);
