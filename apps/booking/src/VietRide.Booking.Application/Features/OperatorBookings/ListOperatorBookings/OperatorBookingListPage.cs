namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed record OperatorBookingListPage(
    IReadOnlyList<OperatorBookingListItem> Items,
    long TotalItems);
