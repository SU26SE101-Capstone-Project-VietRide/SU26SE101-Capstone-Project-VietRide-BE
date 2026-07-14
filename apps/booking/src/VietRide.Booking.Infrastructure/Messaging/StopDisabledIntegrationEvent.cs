using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record StopDisabledIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.stop.disabled";
    public Guid StopId { get; init; }
    public Guid OperatorId { get; init; }
    public Guid? ReplacedByStopId { get; init; }

    [JsonIgnore]
    public Guid EventId => StopId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
