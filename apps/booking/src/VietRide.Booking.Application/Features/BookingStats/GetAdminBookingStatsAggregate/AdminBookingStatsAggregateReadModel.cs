namespace VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

public sealed record AdminBookingStatsAggregateReadModel(
    Guid OperatorId,
    string OperatorName,
    DateOnly? Date,
    int TotalBookings,
    long TotalRevenue,
    int TotalCancellations,
    int TotalNoShows,
    int TotalCompleted);
