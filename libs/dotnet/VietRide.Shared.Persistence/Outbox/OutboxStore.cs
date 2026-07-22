using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Shared.Persistence.Outbox;

/// EF-backed implementation. Resolved per-scope alongside the service's DbContext.
public sealed class OutboxStore : IOutboxStore
{
    private readonly VietRideDbContextBase _db;
    private readonly IClock _clock;

    public OutboxStore(VietRideDbContextBase db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task AddAsync(OutboxEvent outboxEvent, CancellationToken ct = default)
    {
        if (outboxEvent.CreatedAt == default)
        {
            outboxEvent.CreatedAt = _clock.UtcNow;
        }

        await _db.OutboxEvents.AddAsync(outboxEvent, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxEventEnvelope>> FetchPendingAsync(int batchSize, int maxRetryCount, CancellationToken ct)
    {
        var rows = await _db.OutboxEvents
            .Where(x =>
                (x.Status == OutboxEventStatus.PENDING || x.Status == OutboxEventStatus.FAILED)
                && x.RetryCount <= maxRetryCount)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Bridge the DateTimeOffset (entity) → DateTime (envelope) mismatch in
        // memory; NextAttemptAt is always null (no due-time column, option-(a)).
        return rows
            .Select(x => new OutboxEventEnvelope
            {
                Id = x.Id,
                EventType = x.EventType,
                Payload = x.Payload,
                CreatedAt = x.CreatedAt.UtcDateTime,
                PublishedAt = x.PublishedAt?.UtcDateTime,
                RetryCount = x.RetryCount,
                NextAttemptAt = null,
                LastError = x.LastError,
            })
            .ToList();
    }

    public async Task MarkPublishedAsync(Guid id, DateTime publishedAt, CancellationToken ct)
    {
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;
        var schema = GetOutboxSchema();
        await LockSourceAsync(schema, id, ct).ConfigureAwait(false);
        var sql =
            """
            UPDATE "__SCHEMA__".outbox_events source
            SET status = 'PUBLISHED',
                published_at = {1},
                last_error = NULL
            WHERE source.id = {0}
              AND source.status IN ('PENDING', 'FAILED')
              AND NOT EXISTS (
                  SELECT 1
                  FROM "__SCHEMA__".outbox_dlq terminal
                  WHERE terminal.event_id = source.id
              );
            """.Replace("__SCHEMA__", schema, StringComparison.Ordinal);

        await _db.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { id, publishedAt },
            ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task MarkFailedAsync(Guid id, string error, DateTime nextAttemptAt, CancellationToken ct)
    {
        // nextAttemptAt is accepted for signature compatibility but NOT persisted.
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;
        var schema = GetOutboxSchema();
        await LockSourceAsync(schema, id, ct).ConfigureAwait(false);
        var sql =
            """
            UPDATE "__SCHEMA__".outbox_events source
            SET retry_count = source.retry_count + 1,
                last_error = {1},
                status = 'FAILED'
            WHERE source.id = {0}
              AND source.status IN ('PENDING', 'FAILED')
              AND NOT EXISTS (
                  SELECT 1
                  FROM "__SCHEMA__".outbox_dlq terminal
                  WHERE terminal.event_id = source.id
              );
            """.Replace("__SCHEMA__", schema, StringComparison.Ordinal);

        await _db.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { id, error },
            ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task MoveToDlqAsync(Guid id, string error, DateTime terminalAt, CancellationToken ct)
    {
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;
        var schema = GetOutboxSchema();
        await LockSourceAsync(schema, id, ct).ConfigureAwait(false);

        var sql =
            """
            WITH terminal_source AS (
                UPDATE "__SCHEMA__".outbox_events source
                SET retry_count = source.retry_count + 1,
                    last_error = {1},
                    status = 'FAILED'
                WHERE source.id = {0}
                  AND source.status IN ('PENDING', 'FAILED')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "__SCHEMA__".outbox_dlq existing
                      WHERE existing.event_id = source.id
                  )
                RETURNING
                    source.id,
                    source.event_type,
                    source.payload,
                    source.retry_count,
                    source.last_error,
                    source.created_at
            )
            INSERT INTO "__SCHEMA__".outbox_dlq (
                id,
                event_id,
                event_type,
                payload,
                retry_count,
                last_error,
                created_at,
                terminal_at
            )
            SELECT
                gen_random_uuid(),
                source.id,
                source.event_type,
                source.payload,
                source.retry_count,
                source.last_error,
                source.created_at,
                {2}
            FROM terminal_source source
            ON CONFLICT (event_id) DO NOTHING;

            UPDATE "__SCHEMA__".outbox_events source
            SET retry_count = GREATEST(source.retry_count, terminal.retry_count),
                last_error = terminal.last_error,
                status = 'FAILED'
            FROM "__SCHEMA__".outbox_dlq terminal
            WHERE source.id = {0}
              AND terminal.event_id = source.id
              AND source.status <> 'PUBLISHED';
            """.Replace("__SCHEMA__", schema, StringComparison.Ordinal);

        await _db.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { id, error, terminalAt },
            ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task LockSourceAsync(string schema, Guid id, CancellationToken ct)
    {
        var sql =
            """
            SELECT 1
            FROM "__SCHEMA__".outbox_events source
            WHERE source.id = {0}
            FOR UPDATE;
            """.Replace("__SCHEMA__", schema, StringComparison.Ordinal);

        await _db.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { id },
            ct).ConfigureAwait(false);
    }

    private string GetOutboxSchema()
    {
        var schema = _db.Model.FindEntityType(typeof(OutboxEvent))?.GetSchema();
        if (string.IsNullOrWhiteSpace(schema)
            || schema.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException("Outbox entity schema is missing or invalid.");
        }

        return schema;
    }
}
