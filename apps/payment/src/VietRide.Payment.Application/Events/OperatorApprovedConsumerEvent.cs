using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed record OperatorApprovedConsumerEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("approvedAt")] DateTimeOffset ApprovedAt) : IIntegrationEvent
{
    public const string EventTypeValue = "identity.operator.approved";

    [JsonIgnore]
    public DateTime OccurredAt => ApprovedAt.UtcDateTime;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}
