namespace VietRide.Booking.Domain.Enums;

public enum BookingStatus
{
    PENDING_PAYMENT,
    CONFIRMED,
    COMPLETED,
    EXPIRED,
    CANCELLED,
    NO_SHOW,
    PARTIAL_NO_SHOW,
    REFUNDED,
    DISRUPTED,
}
