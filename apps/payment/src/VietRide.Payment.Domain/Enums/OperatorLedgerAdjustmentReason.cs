namespace VietRide.Payment.Domain.Enums;

public enum OperatorLedgerAdjustmentReason
{
    VIETRIDE_FUNDED_VOUCHER_REVERSAL,
    GENERIC_BOOKING_REFUND_ENTITLEMENT,
    MANUAL_WALLET_ADJUSTMENT,
    LEGACY_UNCLASSIFIED,
}
