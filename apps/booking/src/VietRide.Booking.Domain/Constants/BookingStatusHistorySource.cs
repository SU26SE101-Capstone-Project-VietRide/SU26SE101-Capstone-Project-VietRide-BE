namespace VietRide.Booking.Domain.Constants;

public static class BookingStatusHistorySource
{
    public const string CreateBooking = "CREATE_BOOKING";
    public const string CreateRoundTripBooking = "CREATE_ROUND_TRIP_BOOKING";
    public const string ConfirmOnPayment = "CONFIRM_ON_PAYMENT";
    public const string ExpireOnPayment = "EXPIRE_ON_PAYMENT";
    public const string CancelBooking = "CANCEL_BOOKING";
    public const string MarkRefunded = "MARK_REFUNDED";

    private static readonly HashSet<string> Allowed =
    [
        CreateBooking,
        CreateRoundTripBooking,
        ConfirmOnPayment,
        ExpireOnPayment,
        CancelBooking,
        MarkRefunded,
    ];

    public static bool IsDefined(string source) => Allowed.Contains(source);
}
