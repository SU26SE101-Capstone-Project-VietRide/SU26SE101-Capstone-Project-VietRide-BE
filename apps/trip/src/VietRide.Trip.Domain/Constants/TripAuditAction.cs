namespace VietRide.Trip.Domain.Constants;

public static class TripAuditAction
{
    public const string TripCompletedManual = "TRIP_COMPLETED_MANUAL";

    public static bool IsApproved(string action) => action == TripCompletedManual;
}
