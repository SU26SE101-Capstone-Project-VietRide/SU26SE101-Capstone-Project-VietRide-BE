using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public enum ResourceReservationType
{
    CREW,
    VEHICLE,
}

public enum ResourceReservationRole
{
    DRIVER,
    ASSISTANT,
    VEHICLE,
}

public enum ResourceReservationStatus
{
    RESERVED,
    ACTIVE,
    RELEASED,
    CANCELLED,
}

/// <summary>
/// Concrete operational reservation for one crew member or vehicle on a main or shuttle trip.
/// DriverSchedule remains the recurring template; generated trips own concrete reservations.
/// </summary>
public sealed class ResourceReservation : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public ResourceReservationType ResourceType { get; private set; }
    public ResourceReservationRole ResourceRole { get; private set; }
    public Guid ResourceId { get; private set; }
    public Guid? TripId { get; private set; }
    public Guid? ShuttleTripId { get; private set; }
    public DateTimeOffset PlannedStartAt { get; private set; }
    public DateTimeOffset PlannedEndAt { get; private set; }
    public Guid? StartStationId { get; private set; }
    public Guid? EndStationId { get; private set; }
    public decimal? StartLatitude { get; private set; }
    public decimal? StartLongitude { get; private set; }
    public decimal? EndLatitude { get; private set; }
    public decimal? EndLongitude { get; private set; }
    public ResourceReservationStatus Status { get; private set; } = ResourceReservationStatus.RESERVED;
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    private ResourceReservation()
    {
    }

    public static ResourceReservation CreateForTrip(
        Guid operatorId,
        ResourceReservationType resourceType,
        ResourceReservationRole resourceRole,
        Guid resourceId,
        Guid tripId,
        DateTimeOffset plannedStartAt,
        DateTimeOffset plannedEndAt,
        Guid? startStationId,
        Guid? endStationId,
        decimal? startLatitude,
        decimal? startLongitude,
        decimal? endLatitude,
        decimal? endLongitude) =>
        Create(
            operatorId,
            resourceType,
            resourceRole,
            resourceId,
            tripId,
            shuttleTripId: null,
            plannedStartAt,
            plannedEndAt,
            startStationId,
            endStationId,
            startLatitude,
            startLongitude,
            endLatitude,
            endLongitude);

    public static ResourceReservation CreateForShuttleTrip(
        Guid operatorId,
        ResourceReservationType resourceType,
        ResourceReservationRole resourceRole,
        Guid resourceId,
        Guid shuttleTripId,
        DateTimeOffset plannedStartAt,
        DateTimeOffset plannedEndAt,
        Guid? startStationId,
        Guid? endStationId,
        decimal? startLatitude,
        decimal? startLongitude,
        decimal? endLatitude,
        decimal? endLongitude) =>
        Create(
            operatorId,
            resourceType,
            resourceRole,
            resourceId,
            tripId: null,
            shuttleTripId,
            plannedStartAt,
            plannedEndAt,
            startStationId,
            endStationId,
            startLatitude,
            startLongitude,
            endLatitude,
            endLongitude);

    public void UpdatePlan(
        Guid resourceId,
        DateTimeOffset plannedStartAt,
        DateTimeOffset plannedEndAt,
        Guid? startStationId,
        Guid? endStationId,
        decimal? startLatitude,
        decimal? startLongitude,
        decimal? endLatitude,
        decimal? endLongitude)
    {
        EnsureMutable();
        ValidateId(resourceId, nameof(resourceId));
        ValidatePeriod(plannedStartAt, plannedEndAt);
        ValidateCoordinatePair(startLatitude, startLongitude, "start");
        ValidateCoordinatePair(endLatitude, endLongitude, "end");

        ResourceId = resourceId;
        PlannedStartAt = plannedStartAt;
        PlannedEndAt = plannedEndAt;
        StartStationId = startStationId;
        EndStationId = endStationId;
        StartLatitude = startLatitude;
        StartLongitude = startLongitude;
        EndLatitude = endLatitude;
        EndLongitude = endLongitude;
    }

    public bool Activate(DateTimeOffset activatedAt)
    {
        if (Status == ResourceReservationStatus.ACTIVE)
        {
            return false;
        }

        if (Status != ResourceReservationStatus.RESERVED)
        {
            throw new InvalidOperationException("Only reserved resources can be activated.");
        }

        Status = ResourceReservationStatus.ACTIVE;
        ActivatedAt = activatedAt;
        return true;
    }

    public bool Release(DateTimeOffset releasedAt)
    {
        if (Status == ResourceReservationStatus.RELEASED)
        {
            return false;
        }

        if (Status is not (ResourceReservationStatus.RESERVED or ResourceReservationStatus.ACTIVE))
        {
            throw new InvalidOperationException("Only reserved or active resources can be released.");
        }

        Status = ResourceReservationStatus.RELEASED;
        ReleasedAt = releasedAt;
        if (releasedAt > PlannedStartAt && releasedAt < PlannedEndAt)
        {
            PlannedEndAt = releasedAt;
        }
        return true;
    }

    public bool Cancel(DateTimeOffset cancelledAt)
    {
        if (Status == ResourceReservationStatus.CANCELLED)
        {
            return false;
        }

        if (Status is not (ResourceReservationStatus.RESERVED or ResourceReservationStatus.ACTIVE))
        {
            throw new InvalidOperationException("Only reserved or active resources can be cancelled.");
        }

        Status = ResourceReservationStatus.CANCELLED;
        ReleasedAt = cancelledAt;
        return true;
    }

    private static ResourceReservation Create(
        Guid operatorId,
        ResourceReservationType resourceType,
        ResourceReservationRole resourceRole,
        Guid resourceId,
        Guid? tripId,
        Guid? shuttleTripId,
        DateTimeOffset plannedStartAt,
        DateTimeOffset plannedEndAt,
        Guid? startStationId,
        Guid? endStationId,
        decimal? startLatitude,
        decimal? startLongitude,
        decimal? endLatitude,
        decimal? endLongitude)
    {
        ValidateId(operatorId, nameof(operatorId));
        ValidateId(resourceId, nameof(resourceId));
        ValidateExactlyOneSource(tripId, shuttleTripId);
        ValidatePeriod(plannedStartAt, plannedEndAt);
        ValidateCoordinatePair(startLatitude, startLongitude, "start");
        ValidateCoordinatePair(endLatitude, endLongitude, "end");
        ValidateResourceRole(resourceType, resourceRole);

        return new ResourceReservation
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            ResourceType = resourceType,
            ResourceRole = resourceRole,
            ResourceId = resourceId,
            TripId = tripId,
            ShuttleTripId = shuttleTripId,
            PlannedStartAt = plannedStartAt,
            PlannedEndAt = plannedEndAt,
            StartStationId = startStationId,
            EndStationId = endStationId,
            StartLatitude = startLatitude,
            StartLongitude = startLongitude,
            EndLatitude = endLatitude,
            EndLongitude = endLongitude,
        };
    }

    private void EnsureMutable()
    {
        if (Status != ResourceReservationStatus.RESERVED)
        {
            throw new InvalidOperationException("Only reserved resources can change their plan.");
        }
    }

    private static void ValidateExactlyOneSource(Guid? tripId, Guid? shuttleTripId)
    {
        if (tripId.HasValue == shuttleTripId.HasValue
            || tripId == Guid.Empty
            || shuttleTripId == Guid.Empty)
        {
            throw new ArgumentException("Exactly one non-empty assignment source is required.");
        }
    }

    private static void ValidateResourceRole(
        ResourceReservationType resourceType,
        ResourceReservationRole resourceRole)
    {
        if ((resourceType == ResourceReservationType.VEHICLE) != (resourceRole == ResourceReservationRole.VEHICLE))
        {
            throw new ArgumentException("Vehicle resources require the VEHICLE role; crew resources require DRIVER or ASSISTANT.");
        }
    }

    private static void ValidatePeriod(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("Planned end must be later than planned start.", nameof(end));
        }
    }

    private static void ValidateCoordinatePair(decimal? latitude, decimal? longitude, string prefix)
    {
        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ArgumentException($"{prefix} latitude and longitude must be supplied together.");
        }
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
