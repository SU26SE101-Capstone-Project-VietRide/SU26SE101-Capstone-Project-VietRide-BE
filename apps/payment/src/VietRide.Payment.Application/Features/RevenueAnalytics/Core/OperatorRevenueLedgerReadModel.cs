namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record OperatorRevenueLedgerReadModel(
    DateOnly Month,
    Guid? TripId,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    int BookingCount,
    int ParcelCount);
