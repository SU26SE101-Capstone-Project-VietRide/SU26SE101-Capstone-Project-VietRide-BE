using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class TripScheduleChangedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.trip.schedule_changed";

    public TripScheduleChangedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        DateTimeOffset oldDeparture,
        DateTimeOffset newDeparture,
        string severity)
        : base(eventId, occurredAt.UtcDateTime)
    {
        if (severity is not ("MINOR" or "MEDIUM" or "MAJOR"))
        {
            throw new ArgumentException("Schedule change severity is not approved.", nameof(severity));
        }

        TripId = tripId;
        OperatorId = operatorId;
        OldDeparture = oldDeparture;
        NewDeparture = newDeparture;
        Severity = severity;
    }

    public Guid TripId { get; }

    public Guid OperatorId { get; }

    public DateTimeOffset OldDeparture { get; }

    public DateTimeOffset NewDeparture { get; }

    public string Severity { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
