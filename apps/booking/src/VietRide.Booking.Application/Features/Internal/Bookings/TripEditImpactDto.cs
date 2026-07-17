namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record TripEditImpactDto(
    Guid TripId,
    int ActiveBookingCount,
    IReadOnlyList<TripEditImpactDto.ActiveBooking> ActiveBookings)
{
    public sealed record ActiveBooking(
        Guid BookingId,
        string Status,
        IReadOnlyList<string> SeatNumbers);
}
