namespace VietRide.Booking.Application.Features.Bookings.GetBookingStatus;

/// <summary>Minimal Booking-owned projection for post-payment polling.</summary>
public sealed record GetBookingStatusResult(Guid BookingId, string Status);
