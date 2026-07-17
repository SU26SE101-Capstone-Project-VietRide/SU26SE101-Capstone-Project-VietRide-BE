namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record PlatformReportOperatorItem(
    Guid OperatorId,
    string? OperatorName,
    long CompletedBookingCount,
    long CompletedTripCount,
    long DeliveredParcelCount,
    long BookingRevenueVnd,
    long ParcelRevenueVnd,
    long NetRevenueVnd);
