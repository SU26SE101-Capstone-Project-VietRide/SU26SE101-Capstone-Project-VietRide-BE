namespace VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;

public sealed record ResolvePendingActionResult(
    Guid BookingId,
    Guid ActionId,
    string ResolvedAction,
    DateTimeOffset ResolvedAt);
