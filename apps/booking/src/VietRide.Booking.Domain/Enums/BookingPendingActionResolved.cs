namespace VietRide.Booking.Domain.Enums;

public enum BookingPendingActionResolved
{
    ACCEPTED,
    REJECTED,
    AUTO_FALLBACK_DESTINATION,
    AUTO_CANCELLED_NO_SEAT,
    OPERATOR_RESOLVED,
    SUPERSEDED,
}
