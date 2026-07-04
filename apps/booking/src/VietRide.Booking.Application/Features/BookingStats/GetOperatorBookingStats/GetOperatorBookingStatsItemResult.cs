namespace VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;

public sealed record GetOperatorBookingStatsItemResult(
    Guid OperatorId,
    DateOnly Date,
    int TotalBookings,
    long TotalRevenue,
    int TotalCancellations,
    int TotalNoShows,
    int TotalPartialNoShows,
    int TotalCompleted);
