namespace VietRide.Payment.Domain.Enums;

public enum PaymentStatus
{
    PENDING_REDIRECT,
    SUCCEEDED,
    FAILED,
    EXPIRED,
    REFUNDED,
}
