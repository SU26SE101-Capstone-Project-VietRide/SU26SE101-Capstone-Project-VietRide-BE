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

    public async Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        if (outboxEvent.CreatedAt == default)
        {
            outboxEvent.CreatedAt = _clock.UtcNow;
        }

        await _db.OutboxEvents.AddAsync(outboxEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxEvent>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        return await _db.OutboxEvents
            .Where(x => x.Status == OutboxEventStatus.PENDING || x.Status == OutboxEventStatus.FAILED)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
