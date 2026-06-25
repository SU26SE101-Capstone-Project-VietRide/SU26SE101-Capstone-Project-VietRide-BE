namespace VietRide.Booking.Application.Features.Bookings.CancelBooking;

/// <summary>Response for POST /v1/bookings/{bookingId}/cancel.</summary>
public sealed record CancelBookingResult(
    Guid BookingId,
    string Status,
    long RefundAmount,
    string RefundMethod);
