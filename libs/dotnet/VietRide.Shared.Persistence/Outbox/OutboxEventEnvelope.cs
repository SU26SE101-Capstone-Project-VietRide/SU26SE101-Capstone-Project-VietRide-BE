namespace VietRide.Shared.Persistence.Outbox;

/// <summary>
/// Plain projection of one row in the per-service <c>outbox_events</c> table.
/// The persistence layer reads/writes the underlying <see cref="OutboxEvent"/>
/// entity; the outbox worker only sees this broker-publish envelope.
/// </summary>
/// <remarks>
/// Co-located with <see cref="IOutboxStore"/> in the persistence layer so the
/// Messaging worker can consume it without a Persistence→Messaging cycle.
/// </remarks>
public sealed class OutboxEventEnvelope
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int RetryCount { get; set; }

    /// <summary>
    /// Forward-compat slot for a per-row due time. Always <c>null</c> under the
    /// current option-(a) schema (the <see cref="OutboxEvent"/> entity has no
    /// due-time column); retry is bounded purely by poll cadence + RetryCount.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}
