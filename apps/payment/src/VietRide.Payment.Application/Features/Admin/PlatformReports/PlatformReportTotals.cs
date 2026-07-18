namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record PlatformReportTotals(
    long CompletedBookingCount,
    long CompletedTripCount,
    long DeliveredParcelCount,
    long BookingRevenueVnd,
    long ParcelRevenueVnd,
    long NetRevenueVnd);
