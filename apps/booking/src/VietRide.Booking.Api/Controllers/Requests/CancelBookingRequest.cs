namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>Request body for POST /v1/bookings/{bookingId}/cancel.</summary>
public sealed record CancelBookingRequest(string Reason);
