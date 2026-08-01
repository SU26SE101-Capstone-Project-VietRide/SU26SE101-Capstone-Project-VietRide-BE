namespace VietRide.Shared.Messaging.Abstractions;

/// <summary>
/// Handles a consumed integration event delivered at least once from the
/// <c>vietride.events</c> topic exchange.
/// </summary>
/// <remarks>
/// RabbitMQ delivery is at-least-once: handlers MUST be idempotent for their
/// event identity/business key because a message can be re-delivered after a
/// process crash between business work and acknowledgement.
/// </remarks>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>
    /// Processes one integration event instance. Throw
    /// <see cref="TransientIntegrationEventException"/> to request a configured durable,
    /// delayed broker retry; other failures are rejected without requeue for dead-letter handling.
    /// </summary>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
