namespace VietRide.Booking.Application.Features.Bookings.AcceptStopDisabledFallback;

public sealed record AcceptStopDisabledFallbackResult(
    Guid BookingId,
    Guid PendingActionId,
    string ResolvedAction,
    DateTimeOffset ResolvedAt);
