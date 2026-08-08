namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal static class CanonicalRevenueSql
{
    public const string BookingPredicate =
        "reference_type = 'BOOKING' AND (" +
        "entry_type IN ('BOOKING_REVENUE','BOOKING_REFUND','VOUCHER_VIETRIDE_FUNDED_CREDIT') " +
        "OR (entry_type = 'ADJUSTMENT' AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL'))";

    public const string ParcelPredicate =
        "reference_type = 'PARCEL' AND (" +
        "entry_type IN ('PARCEL_REVENUE','PARCEL_REFUND','VOUCHER_VIETRIDE_FUNDED_CREDIT') " +
        "OR (entry_type = 'ADJUSTMENT' AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL'))";

    public const string RecognizedPredicate =
        "((" + BookingPredicate + ") OR (" + ParcelPredicate + "))";

    public const string RefundPredicate =
        "((reference_type = 'BOOKING' AND entry_type = 'BOOKING_REFUND') " +
        "OR (reference_type = 'PARCEL' AND entry_type = 'PARCEL_REFUND'))";
}
