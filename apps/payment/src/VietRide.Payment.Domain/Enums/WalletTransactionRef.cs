namespace VietRide.Payment.Domain.Enums;

public enum WalletTransactionRef
{
    TOP_UP = 1,
    BOOKING_PAYMENT = 2,
    BOOKING_REFUND = 3,
    PARCEL_PAYMENT = 4,
    PARCEL_REFUND = 5,
    PARCEL_ADDITIONAL_PAYMENT = 7,
    MANUAL_ADJUSTMENT = 6,
    PARCEL_COMPENSATION = 8,
}
