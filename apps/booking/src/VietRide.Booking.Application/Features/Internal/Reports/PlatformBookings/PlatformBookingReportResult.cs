namespace VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;

public sealed record PlatformBookingReportResult(IReadOnlyList<PlatformBookingReportItem> Items);
