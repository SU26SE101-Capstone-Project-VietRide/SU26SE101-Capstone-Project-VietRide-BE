namespace VietRide.Shared.Persistence.Outbox;

/// Durable outbox row — INSERTed in same DbContext transaction as business write.
/// Picked up by a background publisher (BACKEND_SOURCE_OF_TRUTH 7.4).
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public OutboxEventStatus Status { get; set; } = OutboxEventStatus.PENDING;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public enum OutboxEventStatus
{
    PENDING,
    PUBLISHING,
    PUBLISHED,
    FAILED
}
