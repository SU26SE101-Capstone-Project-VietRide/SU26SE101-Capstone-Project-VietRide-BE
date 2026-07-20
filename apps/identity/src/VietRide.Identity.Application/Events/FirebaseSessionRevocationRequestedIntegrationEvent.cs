using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

public sealed record FirebaseSessionRevocationRequestedIntegrationEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("reason")] string Reason)
{
    public const string EventType = "identity.firebase_session.revoke_requested";
}
