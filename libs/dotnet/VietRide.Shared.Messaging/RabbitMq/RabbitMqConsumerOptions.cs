namespace VietRide.Shared.Messaging.RabbitMq;

/// <summary>
/// Configuration for one inbound RabbitMQ integration-event consumer.
/// </summary>
public sealed class RabbitMqConsumerOptions
{
    /// <summary>
    /// Durable queue name owned by the consuming service/purpose, e.g.
    /// <c>payment.wallet-bootstrap</c>.
    /// </summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// Routing keys bound to <c>vietride.events</c>. Shape:
    /// <c>&lt;service&gt;.&lt;aggregate&gt;.&lt;verb_past&gt;</c>.
    /// </summary>
    public IReadOnlyCollection<string> BindingKeys { get; set; } = Array.Empty<string>();

    /// <summary>Number of unacknowledged messages allowed per consumer.</summary>
    public ushort PrefetchCount { get; set; } = 1;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueueName))
            throw new InvalidOperationException("RabbitMQ consumer queue name is required.");

        if (BindingKeys.Count == 0 || BindingKeys.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("At least one RabbitMQ consumer binding key is required.");

        if (PrefetchCount == 0)
            throw new InvalidOperationException("RabbitMQ consumer prefetch count must be greater than zero.");
    }
}

/// <summary>
/// Typed options wrapper so multiple event consumers can be registered with
/// independent queue/binding settings in the same process.
/// </summary>
/// <typeparam name="TEvent">Integration event type consumed by the hosted service.</typeparam>
public sealed class RabbitMqConsumerOptions<TEvent>
{
    public RabbitMqConsumerOptions Value { get; set; } = new();
}
