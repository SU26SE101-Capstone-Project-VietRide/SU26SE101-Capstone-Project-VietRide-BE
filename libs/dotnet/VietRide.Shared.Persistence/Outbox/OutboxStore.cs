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
        var row = await _db.OutboxEvents.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        row.Status = OutboxEventStatus.PUBLISHED;
        row.PublishedAt = publishedAt;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(Guid id, string error, DateTime nextAttemptAt, CancellationToken ct)
    {
        // nextAttemptAt is accepted for signature compatibility but NOT persisted:
        // the option-(a) schema has no due-time column. Retry is bounded purely
        // by poll cadence + RetryCount.
        var row = await _db.OutboxEvents.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        row.RetryCount += 1;
        row.LastError = error;
        row.Status = OutboxEventStatus.FAILED;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
