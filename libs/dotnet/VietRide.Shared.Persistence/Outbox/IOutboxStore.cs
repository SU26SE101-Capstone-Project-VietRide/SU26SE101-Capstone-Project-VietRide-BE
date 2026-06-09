namespace VietRide.Shared.Persistence.Outbox;

/// Service-level abstraction over outbox table writes/reads.
/// Application code MUST call AddAsync inside the same DbContext transaction as the business mutation
/// so SaveChanges atomically commits both.
public interface IOutboxStore
{
    Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxEvent>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default);
}
