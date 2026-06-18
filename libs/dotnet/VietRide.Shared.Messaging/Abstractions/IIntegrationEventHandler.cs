using VietRide.Shared.Messaging.Abstractions;

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
    /// Processes one integration event instance. Throw to reject the delivery
    /// with <c>BasicNack(requeue: false)</c> so broker dead-lettering can handle it.
    /// </summary>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
