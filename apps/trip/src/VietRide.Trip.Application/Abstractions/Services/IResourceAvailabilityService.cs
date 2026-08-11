using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Services;

public enum AssignmentSourceType
{
    DRIVER_SCHEDULE,
    TRIP,
    SHUTTLE_TRIP,
}

public enum AvailabilityConflictReason
{
    TIME_OVERLAP,
    TURNAROUND_REQUIRED,
    REPOSITION_REQUIRED,
    RESOURCE_ACTIVE,
}

public sealed record ResourceLocationSnapshot(
    Guid? StationId,
    decimal? Latitude,
    decimal? Longitude);

public sealed record AvailabilityResource(
    ResourceReservationType ResourceType,
    ResourceReservationRole ResourceRole,
    Guid ResourceId);

public sealed record ResourceAvailabilityCandidate(
    Guid OperatorId,
    AssignmentSourceType SourceType,
    Guid? SourceId,
    Guid? ExcludedTripId,
    Guid? ExcludedShuttleTripId,
    DateTimeOffset PlannedStartAt,
    DateTimeOffset PlannedEndAt,
    ResourceLocationSnapshot StartLocation,
    ResourceLocationSnapshot EndLocation,
    IReadOnlyList<AvailabilityResource> Resources);

public sealed record ResourceAvailabilityConflict(
    string ResourceRole,
    Guid ResourceId,
    string Reason,
    string ConflictingSourceType,
    Guid ConflictingSourceId,
    DateTimeOffset SampleRequestedStartAt,
    DateTimeOffset BlockingUntil,
    DateTimeOffset? EarliestFeasibleStartAt,
    int RequiredTravelMinutes,
    int TurnaroundMinutes);

public sealed record ResourceAvailabilityResult(
    bool Available,
    int TurnaroundMinutes,
    IReadOnlyList<ResourceAvailabilityConflict> Conflicts,
    bool HasMore);

public sealed record DriverScheduleAvailabilityInput(
    Guid OperatorId,
    Guid RouteId,
    Guid? VehicleId,
    Guid DriverUserId,
    Guid? AssistantUserId,
    IReadOnlyCollection<int> DayOfWeek,
    TimeOnly DepartureTime,
    DateOnly ValidFrom,
    DateOnly? ValidUntil,
    Guid? ExcludeScheduleId = null,
    bool ExcludePendingTripsFromSchedule = false);

public sealed record ShuttleAvailabilityInput(
    Guid OperatorId,
    Guid MainTripId,
    string Direction,
    Guid DriverUserId,
    Guid VehicleId,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    IReadOnlyList<Guid> OrderedBookingIds,
    Guid? ExcludeShuttleTripId = null);

public sealed record VehicleAssignmentProjection(
    string SourceType,
    Guid SourceId,
    string Status,
    Guid DriverUserId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? StartStationId,
    Guid? EndStationId);

public interface IResourceAvailabilityService
{
    Task<ResourceAvailabilityResult> CheckDriverScheduleAsync(
        DriverScheduleAvailabilityInput input,
        bool acquireLocks,
        CancellationToken cancellationToken = default);

    Task<ResourceAvailabilityResult> CheckShuttleAsync(
        ShuttleAvailabilityInput input,
        bool acquireLocks,
        CancellationToken cancellationToken = default);

    Task<ResourceAvailabilityResult> CheckCandidateAsync(
        ResourceAvailabilityCandidate candidate,
        bool acquireLocks,
        CancellationToken cancellationToken = default);

    Task ReserveTripAsync(
        Domain.Entities.Trip trip,
        CancellationToken cancellationToken = default);

    Task ReserveShuttleTripAsync(
        ShuttleTrip shuttleTrip,
        IReadOnlyList<Guid> orderedBookingIds,
        CancellationToken cancellationToken = default);

    Task RefreshTripAsync(
        Domain.Entities.Trip trip,
        CancellationToken cancellationToken = default);

    Task RefreshTripsAsync(
        IReadOnlyCollection<Domain.Entities.Trip> trips,
        CancellationToken cancellationToken = default);

    Task ActivateTripAsync(Guid tripId, DateTimeOffset activatedAt, CancellationToken cancellationToken = default);

    Task ReleaseTripAsync(Guid tripId, DateTimeOffset releasedAt, CancellationToken cancellationToken = default);

    Task CancelTripAsync(Guid tripId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default);

    Task ActivateShuttleTripAsync(Guid shuttleTripId, DateTimeOffset activatedAt, CancellationToken cancellationToken = default);

    Task ReleaseShuttleTripAsync(Guid shuttleTripId, DateTimeOffset releasedAt, CancellationToken cancellationToken = default);

    Task CancelShuttleTripAsync(Guid shuttleTripId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, (VehicleAssignmentProjection? Current, VehicleAssignmentProjection? Next)>>
        GetVehicleAssignmentsAsync(
            Guid operatorId,
            IReadOnlyCollection<Guid> vehicleIds,
            DateTimeOffset now,
            CancellationToken cancellationToken = default);
}
