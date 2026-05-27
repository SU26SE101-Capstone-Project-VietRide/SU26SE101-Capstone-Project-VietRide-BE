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

    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message.OccurredAt == default)
        {
            message.OccurredAt = _clock.UtcNow;
        }

        await _db.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        return await _db.OutboxMessages
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.OccurredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
