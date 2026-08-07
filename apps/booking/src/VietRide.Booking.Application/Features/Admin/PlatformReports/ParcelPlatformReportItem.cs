namespace VietRide.Booking.Application.Features.Admin.PlatformReports;

public sealed record ParcelPlatformReportItem(
    Guid OperatorId,
    long DeliveredParcelCount);
