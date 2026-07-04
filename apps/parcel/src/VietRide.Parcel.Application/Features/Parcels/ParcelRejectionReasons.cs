namespace VietRide.Parcel.Application.Features.Parcels;

internal static class ParcelRejectionReasons
{
    public const string ReviewTimeout = "PARCEL_REVIEW_TIMEOUT";
    public const string AdditionalPaymentTimeout = "PARCEL_ADDITIONAL_PAYMENT_TIMEOUT";
    public const string LateLoad = "PARCEL_LATE_LOAD";
}
