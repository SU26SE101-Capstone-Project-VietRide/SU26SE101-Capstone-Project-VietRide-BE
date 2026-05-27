namespace VietRide.Shared.Messaging.Outbox;

/// <summary>
/// Marker for the outbox-draining background worker. Lives behind an
/// interface mainly so tests can swap a deterministic implementation.
/// Production registration uses <see cref="OutboxBackgroundService"/>
/// via <c>IHostedService</c>.
/// </summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// Drain one batch synchronously (used by integration tests). Returns
    /// the number of rows successfully published.
    /// </summary>
    Task<int> DrainOnceAsync(CancellationToken ct);
}
