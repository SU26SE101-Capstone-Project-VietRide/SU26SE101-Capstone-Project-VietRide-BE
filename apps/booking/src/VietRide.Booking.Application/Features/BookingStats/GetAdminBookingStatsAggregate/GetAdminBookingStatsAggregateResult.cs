namespace VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

public sealed record GetAdminBookingStatsAggregateResult(
    IReadOnlyList<GetAdminBookingStatsAggregateItemResult> Items,
    int TotalBookings,
    long TotalRevenue);
