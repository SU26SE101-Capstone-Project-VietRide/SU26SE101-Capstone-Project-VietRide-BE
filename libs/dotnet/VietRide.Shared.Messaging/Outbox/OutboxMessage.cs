namespace VietRide.Shared.Messaging.Outbox;

// TODO: wire IOutboxStore from Shared.Persistence when ready.
// This DTO mirrors the schema the persistence agent will own (OutboxEvent
// entity per BACKEND_SOURCE_OF_TRUTH 4.x — table `outbox_events`).
// Keep this stub in sync OR delete once Persistence exposes the canonical
// type — agents that depend on Outbox should import from Persistence then.

/// <summary>
/// Plain DTO representation of one row in the per-service
/// <c>outbox_events</c> table. The persistence layer is responsible for
/// reading/writing the underlying entity; the outbox worker only sees this
/// projection.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
