namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record OperatorRevenueSummaryReadModel(
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long GrossParcelRevenueVnd,
    long ParcelRefundsVnd);
