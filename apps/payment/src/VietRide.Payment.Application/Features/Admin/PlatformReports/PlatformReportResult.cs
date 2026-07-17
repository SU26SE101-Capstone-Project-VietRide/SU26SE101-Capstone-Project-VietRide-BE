namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record PlatformReportResult(
    PlatformReportPeriod Period,
    PlatformReportTotals Totals,
    IReadOnlyList<PlatformReportOperatorItem> ByOperator,
    DateTime GeneratedAt);
