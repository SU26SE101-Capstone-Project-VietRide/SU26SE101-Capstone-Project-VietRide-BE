using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

/// <summary>
/// Consumer-side mirror of Identity's identity.user.created payload.
/// Keep constructor field names and JSON names in sync with the producer.
/// </summary>
public sealed record UserCreatedIntegrationEvent(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt) : IIntegrationEvent
{
    public const string EventType = "identity.user.created";

    [JsonIgnore]
    public Guid EventId => UserId;

    [JsonIgnore]
    public DateTime OccurredAt => CreatedAt.UtcDateTime;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
