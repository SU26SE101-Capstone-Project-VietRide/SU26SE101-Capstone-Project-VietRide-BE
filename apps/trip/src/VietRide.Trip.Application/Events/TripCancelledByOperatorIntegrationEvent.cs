using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class TripCancelledByOperatorIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.trip.cancelled";

    public TripCancelledByOperatorIntegrationEvent(
        Guid tripId,
        Guid operatorId,
        DateTimeOffset cancelledAt,
        string cancelReason)
        : this(Guid.NewGuid(), cancelledAt, tripId, operatorId, cancelledAt, cancelReason)
    {
    }

    public TripCancelledByOperatorIntegrationEvent(
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
            : cancelReason.Trim();
    }

    public Guid TripId { get; }
    public Guid OperatorId { get; }
    public DateTimeOffset CancelledAt { get; }
    public string CancelReason { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
