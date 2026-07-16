using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class TripCancelledIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.trip.cancelled";
    public const string DriverScheduleDayRemovedReason = "DRIVER_SCHEDULE_DAY_REMOVED";

    public TripCancelledIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        DateTimeOffset cancelledAt,
        string cancelReason)
        : base(eventId, occurredAt.UtcDateTime)
    {
        TripId = tripId;
        OperatorId = operatorId;
        CancelledAt = cancelledAt;
        CancelReason = string.IsNullOrWhiteSpace(cancelReason)
            ? throw new ArgumentException("Cancellation reason is required.", nameof(cancelReason))
            : cancelReason;
    }

    public Guid TripId { get; }

    public Guid OperatorId { get; }

    public DateTimeOffset CancelledAt { get; }

    public string CancelReason { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
