using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

/// <summary>
/// Integration event emitted when a passenger user is created (BSOT §7.3).
/// Serializes to camelCase keys: { userId, role, email, createdAt }.
/// </summary>
public sealed record UserCreatedIntegrationEvent(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt)
{
    public const string EventType = "identity.user.created";
}
