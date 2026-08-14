namespace VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;

public sealed record OperatorBookingStatsReadModel(
    Guid OperatorId,
    DateOnly Date,
    int TotalBookings,
    long TotalRevenue,
    int TotalCancellations,
    int TotalNoShows,
    int TotalCompleted,
    int NoShowPassengerCount = 0);
