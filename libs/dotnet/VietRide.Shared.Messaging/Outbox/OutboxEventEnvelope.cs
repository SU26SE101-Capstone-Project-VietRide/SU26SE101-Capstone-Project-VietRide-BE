namespace VietRide.Shared.Messaging.Outbox;

// TODO: wire IOutboxStore from Shared.Persistence when ready.
// This DTO mirrors the schema owned by the persistence layer (OutboxEvent
// entity per BACKEND_SOURCE_OF_TRUTH 4.x — table `outbox_events`).
// Keep this stub in sync OR delete once Messaging imports the canonical
// projection directly from Persistence.

/// <summary>
/// Plain projection of one row in the per-service <c>outbox_events</c> table.
/// The persistence layer is responsible for reading/writing the underlying
/// entity; the outbox worker only sees this broker-publish envelope.
/// </summary>
public sealed class OutboxEventEnvelope
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
