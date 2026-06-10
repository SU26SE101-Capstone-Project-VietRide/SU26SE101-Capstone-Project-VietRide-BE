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
    /// Enqueue an integration event (added to the ambient EF transaction; it is
    /// committed by the caller's unit-of-work SaveChanges).
    /// </summary>
    /// <param name="eventType">Logical event type / routing key.</param>
    /// <param name="payloadJson">Already-serialized JSON payload.</param>
    Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default);
}
