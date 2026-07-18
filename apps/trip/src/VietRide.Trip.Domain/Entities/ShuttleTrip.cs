using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class ShuttleTrip : BaseEntity<Guid>
{
    public const string InboundDirection = "INBOUND_TO_STATION";
    public const string ScheduledStatus = "SCHEDULED";
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

    private ShuttleTrip() { }

    public static ShuttleTrip Create(
        Guid operatorId,
        Guid mainTripId,
        Guid stationId,
        Guid driverUserId,
        Guid vehicleId,
        DateTimeOffset scheduledDepartureTime,
        DateTimeOffset scheduledEndTime,
        string? notes)
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

        return new ShuttleTrip
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            MainTripId = mainTripId,
            StationId = stationId,
            Direction = InboundDirection,
            DriverUserId = driverUserId,
            VehicleId = vehicleId,
            ScheduledDepartureTime = scheduledDepartureTime,
            ScheduledEndTime = scheduledEndTime,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
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
