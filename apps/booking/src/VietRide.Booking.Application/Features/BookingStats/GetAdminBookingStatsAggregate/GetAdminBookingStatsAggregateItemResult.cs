namespace VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

public sealed record GetAdminBookingStatsAggregateItemResult(
    Guid OperatorId,
    string OperatorName,
    DateOnly? Date,
    int TotalBookings,
    long TotalRevenue,
    int TotalCancellations,
    int TotalNoShows,
    int TotalPartialNoShows,
    int TotalCompleted);
