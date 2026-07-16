namespace VietRide.Trip.Domain.Constants;

public static class TripAuditAction
{
    public const string TripCompletedManual = "TRIP_COMPLETED_MANUAL";
    public const string TripEdited = "TRIP_EDITED";
    public const string TripVehicleSwapped = "TRIP_VEHICLE_SWAPPED";
    public const string TripRouteChanged = "TRIP_ROUTE_CHANGED";
    public const string DriverScheduleCascadeApplied = "DRIVER_SCHEDULE_CASCADE_APPLIED";

    public static bool IsApproved(string action) => action is
        TripCompletedManual
        or TripEdited
        or TripVehicleSwapped
        or TripRouteChanged
        or DriverScheduleCascadeApplied;
}
