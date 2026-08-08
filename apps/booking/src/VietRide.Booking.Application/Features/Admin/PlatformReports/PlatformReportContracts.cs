namespace VietRide.Booking.Application.Features.Admin.PlatformReports;

public sealed record PlatformReportPeriod(DateOnly From, DateOnly To, string Timezone);

public sealed record PlatformReportTotals(
    long CompletedBookingCount,
    long CompletedTripCount,
    long DeliveredParcelCount,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long NetTransportRevenueVnd);

public sealed record PlatformReportOperatorItem(
    Guid OperatorId,
    string? OperatorName,
    long CompletedBookingCount,
    long CompletedTripCount,
    long DeliveredParcelCount,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long NetTransportRevenueVnd);

public sealed record PlatformReportResult(
    PlatformReportPeriod Period,
    PlatformReportTotals Totals,
    IReadOnlyList<PlatformReportOperatorItem> ByOperator,
    DateTime GeneratedAt);
