namespace VietRide.Shared.Messaging.Outbox;

// TODO: wire IOutboxStore from Shared.Persistence when ready.
// Persistence will provide the canonical implementation (backed by
// `outbox_events` table per service). Until then this interface lives in
// Messaging so the BackgroundService compiles; services register their
// own EfCore-backed impl in Infrastructure.

/// <summary>
/// Persistence-agnostic accessor for the per-service outbox table. Reads
/// pending rows, marks them processed, and records transient failures.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Fetch up to <paramref name="batchSize"/> rows where
    /// <c>ProcessedAt IS NULL</c> AND
    /// (<c>NextAttemptAt IS NULL OR NextAttemptAt &lt;= NOW</c>),
    /// ordered by <c>OccurredAt ASC</c>. Should use
    /// <c>FOR UPDATE SKIP LOCKED</c> on Postgres to allow multiple workers.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken ct);

    /// <summary>Mark a row as successfully published.</summary>
    Task MarkProcessedAsync(Guid id, DateTime processedAt, CancellationToken ct);

    /// <summary>
    /// Record a failed publish — increment RetryCount, push NextAttemptAt
    /// forward by the supplied backoff, store the error message.
    /// </summary>
    Task MarkFailedAsync(
        Guid id,
        string error,
        DateTime nextAttemptAt,
        CancellationToken ct);
}
