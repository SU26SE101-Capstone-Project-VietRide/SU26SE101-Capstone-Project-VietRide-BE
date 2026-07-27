namespace VietRide.Parcel.Application.Features.Parcels;

internal static class ParcelRejectionReasons
{
    public const string ReviewTimeout = "OPERATOR_REVIEW_TIMEOUT";
    public const string AdditionalPaymentTimeout = "PARCEL_ADDITIONAL_PAYMENT_TIMEOUT";
    public const string CheckInTimeout = "CHECK_IN_TIMEOUT";
    public const string FinalPaymentTimeout = "FINAL_PAYMENT_TIMEOUT";
    public const string LateLoad = "PARCEL_LATE_LOAD";
}
