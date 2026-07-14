using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

/// <summary>
/// Integration event emitted when an operator is approved (BSOT §7.3).
/// Serializes to camelCase keys: { operatorId, approvedAt }.
/// </summary>
public sealed record OperatorApprovedIntegrationEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("approvedAt")] DateTimeOffset ApprovedAt)
{
    public const string EventType = "identity.operator.approved";
}
