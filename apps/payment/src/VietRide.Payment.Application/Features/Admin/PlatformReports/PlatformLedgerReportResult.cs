namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record PlatformLedgerReportResult(
    IReadOnlyList<PlatformLedgerReportItem> Items);
