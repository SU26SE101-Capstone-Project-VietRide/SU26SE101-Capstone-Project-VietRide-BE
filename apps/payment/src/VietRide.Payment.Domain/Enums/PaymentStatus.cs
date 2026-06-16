namespace VietRide.Payment.Domain.Enums;

public enum PaymentStatus
{
    PENDING_REDIRECT = 1,
    SUCCEEDED = 2,
    FAILED = 3,
    EXPIRED = 4,
    REFUNDED = 5,
}
