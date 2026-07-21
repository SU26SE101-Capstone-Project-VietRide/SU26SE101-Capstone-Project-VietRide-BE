namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record PlatformLedgerReportItem(
    Guid OperatorId,
    long BookingRevenueVnd,
    long ParcelRevenueVnd);
