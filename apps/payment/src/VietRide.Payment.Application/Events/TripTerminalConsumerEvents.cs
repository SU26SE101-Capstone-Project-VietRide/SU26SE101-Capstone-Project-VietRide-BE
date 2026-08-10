using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed record TripCompletedConsumerEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("tripId")] Guid TripId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("terminalAt")] DateTimeOffset TerminalAt,
    [property: JsonPropertyName("hasSubstitution")] bool HasSubstitution) : IIntegrationEvent
{
    public const string EventTypeValue = "trip.trip.completed";
    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
    [JsonIgnore]
    DateTime IIntegrationEvent.OccurredAt => OccurredAt.UtcDateTime;
}

public sealed record TripDisruptedConsumerEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("tripId")] Guid TripId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("terminalAt")] DateTimeOffset TerminalAt,
    [property: JsonPropertyName("hasSubstitution")] bool HasSubstitution,
    [property: JsonPropertyName("reason")] string? Reason = null) : IIntegrationEvent
{
    public const string EventTypeValue = "trip.trip.disrupted";
    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
    [JsonIgnore]
    DateTime IIntegrationEvent.OccurredAt => OccurredAt.UtcDateTime;
}
