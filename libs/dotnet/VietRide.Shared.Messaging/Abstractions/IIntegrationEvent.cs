namespace VietRide.Shared.Messaging.Abstractions;

/// <summary>
/// Marker contract for cross-service integration events published via
/// RabbitMQ topic exchange <c>vietride.events</c>. Per
/// BACKEND_SOURCE_OF_TRUTH section 4.3 (Event consume) + 11.x messaging.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Unique event id (becomes RabbitMQ MessageId for dedupe).</summary>
    Guid EventId { get; }

    /// <summary>UTC instant the event was produced by the source aggregate.</summary>
    DateTime OccurredAt { get; }

    /// <summary>
    /// Event type — used as the AMQP routing key on
    /// <c>vietride.events</c>. Convention: <c>&lt;service&gt;.&lt;aggregate&gt;.&lt;verb&gt;</c>
    /// e.g. <c>booking.booking.confirmed</c>.
    /// </summary>
    string EventType { get; }
}
