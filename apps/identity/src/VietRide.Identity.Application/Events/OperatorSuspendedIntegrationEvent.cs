using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

/// <summary>
/// Integration event emitted when an operator is suspended (BSOT §7.3).
/// Serializes to camelCase keys: { operatorId, suspendedAt }.
/// </summary>
public sealed record OperatorSuspendedIntegrationEvent(
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("suspendedAt")] DateTimeOffset SuspendedAt)
{
    public const string EventType = "identity.operator.suspended";
}
