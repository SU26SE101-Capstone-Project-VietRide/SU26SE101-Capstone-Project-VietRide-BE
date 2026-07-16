namespace VietRide.Trip.Domain.Constants;

public static class DriverScheduleAuditAction
{
    public const string DriverScheduleEdited = "DRIVER_SCHEDULE_EDITED";

    public static bool IsApproved(string action) => action == DriverScheduleEdited;
}
