namespace VietRide.Shared.Persistence.Outbox;

/// Durable outbox row — INSERTed in same DbContext transaction as business write.
/// Picked up by a background publisher (BACKEND_SOURCE_OF_TRUTH 7.4).
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
