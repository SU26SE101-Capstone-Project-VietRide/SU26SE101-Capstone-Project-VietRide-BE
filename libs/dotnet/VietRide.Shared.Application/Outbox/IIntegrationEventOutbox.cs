namespace VietRide.Shared.Application.Outbox;

/// <summary>
/// Application-facing seam for enqueuing an integration event into the
/// transactional outbox. Intentionally string-based: it must NOT reference any
/// Shared.Messaging or Shared.Persistence type. Messaging already references
/// Application, so an Application→Messaging edge would introduce a cycle; the
/// Persistence implementation maps these strings onto the outbox entity.
/// </summary>
public interface IIntegrationEventOutbox
{
    /// <summary>
    /// Enqueue an integration event with an identity allocated by the producer.
    /// The supplied id is persisted as the outbox row id and later becomes the
    /// broker MessageId.
    /// </summary>
    /// <param name="eventId">Producer-allocated, non-empty event identity.</param>
    /// <param name="eventType">Logical event type / routing key.</param>
    /// <param name="payloadJson">Already-serialized JSON payload.</param>
    Task EnqueueAsync(
        Guid eventId,
        string eventType,
        string payloadJson,
        CancellationToken ct = default)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event id must not be empty.", nameof(eventId));
        }

        throw new NotSupportedException(
            "This outbox implementation does not support producer-supplied event identities.");
    }

    /// <summary>
    /// Enqueue an integration event (added to the ambient EF transaction; it is
    /// committed by the caller's unit-of-work SaveChanges). Implementations use
    /// a valid payload <c>eventId</c> as the canonical identity when present;
    /// otherwise they allocate and persist a new identity in the payload.
    /// </summary>
    /// <param name="eventType">Logical event type / routing key.</param>
    /// <param name="payloadJson">Already-serialized JSON payload.</param>
    Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default);
}
