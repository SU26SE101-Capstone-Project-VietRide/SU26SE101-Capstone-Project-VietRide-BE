namespace VietRide.Shared.Messaging.Abstractions;

/// <summary>
/// Publishes an <see cref="IIntegrationEvent"/> to the broker. The default
/// implementation (<c>RabbitMqEventPublisher</c>) writes to topic exchange
/// <c>vietride.events</c> using <see cref="IIntegrationEvent.EventType"/>
/// as the routing key.
/// </summary>
/// <remarks>
/// Application/domain code SHOULD NOT call this directly — write the event
/// to <c>OutboxMessage</c> inside the same DbContext transaction and let
/// <c>OutboxBackgroundService</c> drain it. This interface is exposed so
/// the outbox worker (and integration tests) can publish.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publish an event to the broker. Throws on transport failure so the
    /// outbox worker can leave the row unprocessed and retry.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct)
        where TEvent : IIntegrationEvent;

    /// <summary>
    /// Publish a pre-serialized envelope. Used by the outbox worker which
    /// has the JSON payload + routing key already on the
    /// <c>OutboxMessage</c> row and avoids re-serializing.
    /// </summary>
    Task PublishRawAsync(
        string routingKey,
        Guid messageId,
        string payloadJson,
        CancellationToken ct);
}
