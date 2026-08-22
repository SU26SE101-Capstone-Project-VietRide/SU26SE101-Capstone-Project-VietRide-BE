using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class ShuttleTrip : BaseEntity<Guid>
{
    public const string InboundDirection = "INBOUND_TO_STATION";
    public const string OutboundDirection = "OUTBOUND_FROM_STATION";
    public const string ScheduledStatus = "SCHEDULED";
    public const string InProgressStatus = "IN_PROGRESS";
    public const string CompletedStatus = "COMPLETED";
    public const string CancelledStatus = "CANCELLED";
    public Guid OperatorId { get; private set; }
    public Guid MainTripId { get; private set; }
    public Guid StationId { get; private set; }
    public string Direction { get; private set; } = InboundDirection;
    public Guid DriverUserId { get; private set; }
    public Guid VehicleId { get; private set; }
    public string Status { get; private set; } = ScheduledStatus;
    public DateTimeOffset ScheduledDepartureTime { get; private set; }
    public DateTimeOffset ScheduledEndTime { get; private set; }
    public DateTimeOffset? ActualDepartureTime { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Notes { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }
    public Guid? CancelledByUserId { get; private set; }

    private ShuttleTrip() { }

    public static ShuttleTrip Create(
        Guid operatorId,
        Guid mainTripId,
        Guid stationId,
        Guid driverUserId,
        Guid vehicleId,
        DateTimeOffset scheduledDepartureTime,
        DateTimeOffset scheduledEndTime,
        string? notes,
        string direction = InboundDirection,
        Guid? createdByUserId = null)
    {
        ValidateId(operatorId, nameof(operatorId));
        ValidateId(mainTripId, nameof(mainTripId));
        ValidateId(stationId, nameof(stationId));
        ValidateId(driverUserId, nameof(driverUserId));
        ValidateId(vehicleId, nameof(vehicleId));
        if (scheduledEndTime <= scheduledDepartureTime)
        {
            throw new ArgumentException("Scheduled end time must be after departure time.", nameof(scheduledEndTime));
        }

        if (direction is not (InboundDirection or OutboundDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Created-by user ID cannot be empty.", nameof(createdByUserId));
        }

        return new ShuttleTrip
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            MainTripId = mainTripId,
            StationId = stationId,
            Direction = direction,
            DriverUserId = driverUserId,
            VehicleId = vehicleId,
            ScheduledDepartureTime = scheduledDepartureTime,
            ScheduledEndTime = scheduledEndTime,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = createdByUserId,
        };
    }

    public bool Start(DateTimeOffset startedAt)
    {
        if (Status == InProgressStatus)
        {
            return false;
        }

        if (Status != ScheduledStatus)
        {
            throw new InvalidOperationException("Only scheduled Shuttle trips can start.");
        }

        Status = InProgressStatus;
        ActualDepartureTime = startedAt;
        return true;
    }

    public bool Complete(DateTimeOffset completedAt)
    {
        if (Status == CompletedStatus)
        {
            return false;
        }

        if (Status != InProgressStatus)
        {
            throw new InvalidOperationException("Only in-progress Shuttle trips can complete.");
        }

        Status = CompletedStatus;
        CompletedAt = completedAt;
        return true;
    }

    public void ChangeAssignment(Guid driverUserId, Guid vehicleId)
    {
        if (Status != ScheduledStatus)
        {
            throw new InvalidOperationException("Only scheduled Shuttle trips can be reassigned.");
        }

        ValidateId(driverUserId, nameof(driverUserId));
        ValidateId(vehicleId, nameof(vehicleId));
        DriverUserId = driverUserId;
        VehicleId = vehicleId;
    }

    public bool Cancel(DateTimeOffset cancelledAt, Guid cancelledByUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));
        }

        ValidateId(cancelledByUserId, nameof(cancelledByUserId));

        if (Status == CancelledStatus)
        {
            return false;
        }

        if (Status is not (ScheduledStatus or InProgressStatus))
        {
            throw new InvalidOperationException("Only scheduled or in-progress Shuttle trips can be cancelled.");
        }

        Status = CancelledStatus;
        CancelledAt = cancelledAt;
        CancelReason = reason.Trim();
        CancelledByUserId = cancelledByUserId;
        return true;
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    public bool RelinkStation(Guid duplicateStationId, Guid primaryStationId)
    {
        ValidateId(duplicateStationId, nameof(duplicateStationId));
        ValidateId(primaryStationId, nameof(primaryStationId));
        if (duplicateStationId == primaryStationId)
            throw new ArgumentException("Station merge IDs must be different.", nameof(primaryStationId));

        if (StationId != duplicateStationId)
            return false;

        StationId = primaryStationId;
        return true;
    }
}
