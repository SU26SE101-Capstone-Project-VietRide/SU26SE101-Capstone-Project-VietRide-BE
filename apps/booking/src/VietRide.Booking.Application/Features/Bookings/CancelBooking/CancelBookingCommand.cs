using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.CancelBooking;

/// <summary>
/// Command for POST /v1/bookings/{bookingId}/cancel.
/// Refund is delivered asynchronously by the Payment booking.booking.cancelled consumer.
/// </summary>
public sealed record CancelBookingCommand(
    Guid BookingId,
    Guid PassengerUserId,
    string IdempotencyKey,
    string Reason) : IRequest<CancelBookingResult>;
