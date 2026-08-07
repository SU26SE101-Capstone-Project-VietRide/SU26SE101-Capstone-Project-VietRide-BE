namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record PaymentRevenueSummaryDto(
    long TotalProjectRevenueVnd,
    long NetTransportRevenueVnd,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long SubscriptionRevenueVnd);
