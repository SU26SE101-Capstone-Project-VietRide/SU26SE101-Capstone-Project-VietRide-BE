namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record BookingPlatformReportItem(
    Guid OperatorId,
    long CompletedBookingCount,
    long BookingRevenueVnd);
