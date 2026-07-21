namespace VietRide.Booking.Application.Features.Admin.PlatformReports;

public sealed record PlatformReportPeriod(DateTime From, DateTime To, string Timezone);

public sealed record PlatformReportTotals(
    long CompletedBookingCount,
    long CompletedTripCount,
    long DeliveredParcelCount,
    long BookingRevenueVnd,
    long ParcelRevenueVnd,
    long NetRevenueVnd);

public sealed record PlatformReportOperatorItem(
    Guid OperatorId,
    string? OperatorName,
    long CompletedBookingCount,
    long CompletedTripCount,
    long DeliveredParcelCount,
    long BookingRevenueVnd,
    long ParcelRevenueVnd,
    long NetRevenueVnd);

public sealed record PlatformReportResult(
    PlatformReportPeriod Period,
    PlatformReportTotals Totals,
    IReadOnlyList<PlatformReportOperatorItem> ByOperator,
    DateTime GeneratedAt);
