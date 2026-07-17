namespace VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;

public sealed record PlatformBookingReportItem(
    Guid OperatorId,
    long CompletedBookingCount,
    long BookingRevenueVnd);
