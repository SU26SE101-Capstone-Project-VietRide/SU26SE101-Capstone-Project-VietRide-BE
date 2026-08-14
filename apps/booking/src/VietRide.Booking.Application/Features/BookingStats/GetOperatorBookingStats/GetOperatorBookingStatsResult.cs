namespace VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;

public sealed record GetOperatorBookingStatsResult(
    IReadOnlyList<GetOperatorBookingStatsItemResult> Items,
    int TotalBookings,
    int NoShowPassengerCount);
