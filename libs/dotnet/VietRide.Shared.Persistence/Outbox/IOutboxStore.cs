namespace VietRide.Shared.Persistence.Outbox;

/// <summary>
/// Canonical persistence-layer accessor for the per-service <c>outbox_events</c>
/// table. Application code MUST call <see cref="AddAsync"/> inside the same
/// DbContext transaction as the business mutation so SaveChanges atomically
/// commits both. The outbox worker reads pending rows, marks them published,
/// and records failures.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Enlist a new outbox row in the ambient EF transaction (added to the
    /// DbContext; committed by the caller's SaveChanges).
    /// </summary>
    Task AddAsync(OutboxEvent outboxEvent, CancellationToken ct = default);

    /// <summary>
    /// Fetch up to <paramref name="batchSize"/> rows where
    /// <c>Status IN ('PENDING','FAILED')</c> and <c>RetryCount &lt;= maxRetryCount</c>,
    /// ordered by <c>CreatedAt ASC</c>, projected to broker-publish envelopes.
    /// Retry is bounded by poll cadence + <paramref name="maxRetryCount"/>;
    /// there is no per-row due-time gate.
    /// </summary>
    Task<IReadOnlyList<OutboxEventEnvelope>> FetchPendingAsync(int batchSize, int maxRetryCount, CancellationToken ct);

    /// <summary>Mark an outbox event as successfully published.</summary>
    Task MarkPublishedAsync(Guid id, DateTime publishedAt, CancellationToken ct);

    /// <summary>
    /// Record a failed publish — increment RetryCount, set Status=FAILED and
    /// store the error. <paramref name="nextAttemptAt"/> is accepted for
    /// signature compatibility but NOT persisted (no due-time column exists).
    /// </summary>
    Task MarkFailedAsync(Guid id, string error, DateTime nextAttemptAt, CancellationToken ct);
}
