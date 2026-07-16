namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public sealed record TripBookingImpactProjection(
    Guid TripId,
    int ActiveBookingCount,
    IReadOnlyList<TripBookingImpactProjection.ActiveBooking> ActiveBookings)
{
    public sealed record ActiveBooking(
        Guid BookingId,
        string Status,
        IReadOnlyList<string> SeatNumbers);
}
