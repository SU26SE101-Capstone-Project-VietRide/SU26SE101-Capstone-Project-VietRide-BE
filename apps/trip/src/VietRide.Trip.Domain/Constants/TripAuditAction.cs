namespace VietRide.Trip.Domain.Constants;

public static class TripAuditAction
{
    public const string TripCompletedManual = "TRIP_COMPLETED_MANUAL";
    public const string TripEdited = "TRIP_EDITED";
    public const string TripVehicleSwapped = "TRIP_VEHICLE_SWAPPED";
    public const string TripRouteChanged = "TRIP_ROUTE_CHANGED";
    public const string DriverScheduleCascadeApplied = "DRIVER_SCHEDULE_CASCADE_APPLIED";
    public const string VehicleSubstitutionTriggered = "VEHICLE_SUBSTITUTION_TRIGGERED";
    public const string RouteChangeProposalCreated = "ROUTE_CHANGE_PROPOSAL_CREATED";
    public const string RouteChangeProposalApproved = "ROUTE_CHANGE_PROPOSAL_APPROVED";
    public const string RouteChangeProposalRejected = "ROUTE_CHANGE_PROPOSAL_REJECTED";
    public const string RouteChangeProposalSuperseded = "ROUTE_CHANGE_PROPOSAL_SUPERSEDED";
    public const string RouteChangeProposalExpired = "ROUTE_CHANGE_PROPOSAL_EXPIRED";
    public const string TripSeatDisabled = "TRIP_SEAT_DISABLED";
    public const string TripSeatEnabled = "TRIP_SEAT_ENABLED";
    public const string TripStopSnapshotSynced = "TRIP_STOP_SNAPSHOT_SYNCED";

    public static bool IsApproved(string action) => action is
        TripCompletedManual
        or TripEdited
        or TripVehicleSwapped
        or TripRouteChanged
        or DriverScheduleCascadeApplied
        or VehicleSubstitutionTriggered
        or RouteChangeProposalCreated
        or RouteChangeProposalApproved
        or RouteChangeProposalRejected
        or RouteChangeProposalSuperseded
        or RouteChangeProposalExpired
        or TripSeatDisabled
        or TripSeatEnabled
        or TripStopSnapshotSynced;
}
