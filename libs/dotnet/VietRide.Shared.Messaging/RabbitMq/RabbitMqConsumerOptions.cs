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

    /// <summary>
    /// Dead-letter exchange used when handler failures are nacked without requeue.
    /// Defaults to a deterministic queue-scoped exchange.
    /// </summary>
    public string? DeadLetterExchangeName { get; set; }

    /// <summary>
    /// Dead-letter queue receiving failed deliveries. Defaults to <c>{QueueName}.dlq</c>.
    /// </summary>
    public string? DeadLetterQueueName { get; set; }

    /// <summary>
    /// Routing key used from the source queue to the DLX and bound by the DLQ.
    /// Defaults to <c>{QueueName}.dead</c>.
    /// </summary>
    public string? DeadLetterRoutingKey { get; set; }

    public string ResolvedDeadLetterExchangeName => string.IsNullOrWhiteSpace(DeadLetterExchangeName)
        ? $"{QueueName}.dlx"
        : DeadLetterExchangeName.Trim();

    public string ResolvedDeadLetterQueueName => string.IsNullOrWhiteSpace(DeadLetterQueueName)
        ? $"{QueueName}.dlq"
        : DeadLetterQueueName.Trim();

    public string ResolvedDeadLetterRoutingKey => string.IsNullOrWhiteSpace(DeadLetterRoutingKey)
        ? $"{QueueName}.dead"
        : DeadLetterRoutingKey.Trim();

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
