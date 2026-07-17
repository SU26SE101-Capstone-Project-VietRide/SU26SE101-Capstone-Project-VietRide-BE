using VietRide.Shared.Application.Outbox;

namespace VietRide.Shared.Persistence.Outbox;

/// <summary>
/// Persistence-side implementation of <see cref="IIntegrationEventOutbox"/>.
/// Maps the string-based application seam onto an <see cref="OutboxEvent"/> row
/// and enlists it in the ambient EF transaction via <see cref="IOutboxStore.AddAsync"/>.
/// </summary>
public sealed class IntegrationEventOutbox : IIntegrationEventOutbox
{
    private readonly IOutboxStore _store;

    public IntegrationEventOutbox(IOutboxStore store)
    {
        _store = store;
    }

    public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        => EnqueueAsync(Guid.NewGuid(), eventType, payloadJson, ct);

    public Task EnqueueAsync(
        Guid eventId,
        string eventType,
        string payloadJson,
        CancellationToken ct = default)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event id must not be empty.", nameof(eventId));
        }

        // Status defaults to PENDING and RetryCount to 0 (entity initializers);
        // CreatedAt is stamped by OutboxStore.AddAsync from IClock.
        var outboxEvent = new OutboxEvent
        {
            Id = eventId,
            EventType = eventType,
            Payload = payloadJson,
        };

        return _store.AddAsync(outboxEvent, ct);
    }
}
