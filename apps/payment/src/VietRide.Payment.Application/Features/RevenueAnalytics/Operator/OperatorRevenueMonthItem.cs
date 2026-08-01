namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRevenueMonthItem(
    string Month,
    long RevenueVnd,
    long TicketRevenueVnd,
    long ParcelRevenueVnd,
    int TripCount);
