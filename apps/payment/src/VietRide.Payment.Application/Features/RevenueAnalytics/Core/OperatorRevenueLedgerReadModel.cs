namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record OperatorRevenueLedgerReadModel(
    DateOnly Month,
    Guid? TripId,
    long TicketRevenueVnd,
    long ParcelRevenueVnd,
    int BookingCount,
    int ParcelCount);
