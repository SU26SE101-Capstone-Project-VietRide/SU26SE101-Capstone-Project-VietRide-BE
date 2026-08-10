using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using VietRide.Shared.Kernel.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Shared.Messaging.RabbitMq;

/// <summary>
/// Publishes integration events to the <c>vietride.events</c> topic
/// exchange. Routing key = <see cref="IIntegrationEvent.EventType"/>;
/// payload = UTF-8 JSON; delivery mode = persistent (2);
/// <c>MessageId</c> = <see cref="IIntegrationEvent.EventId"/>.
/// </summary>
public sealed class RabbitMqEventPublisher : IEventPublisher
{
    private readonly IRabbitMqConnectionFactory _connections;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly object _channelLock = new();
    private IModel? _channel;
    private bool _exchangeDeclared;

    public RabbitMqEventPublisher(
        IRabbitMqConnectionFactory connections,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connections = connections;
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(evt);
        var json = JsonSerializer.Serialize(evt, evt.GetType(), UtcJson.IgnoreNullOptions);
        return PublishRawAsync(evt.EventType, evt.EventId, json, ct);
    }

    public Task PublishRawAsync(
        string routingKey,
        Guid messageId,
        string payloadJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(routingKey))
            throw new ArgumentException("Routing key required.", nameof(routingKey));

        ct.ThrowIfCancellationRequested();

        var channel = EnsureChannel();
        var body = Encoding.UTF8.GetBytes(UtcJson.NormalizeInstants(payloadJson));

        var props = channel.CreateBasicProperties();
        props.ContentType = "application/json";
        props.ContentEncoding = "utf-8";
        props.DeliveryMode = 2; // persistent
        props.MessageId = messageId.ToString();
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        props.Type = routingKey;

        lock (_channelLock)
        {
            channel.BasicPublish(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props,
                body: body);
        }

        _logger.LogDebug(
            "Published event {RoutingKey} (MessageId={MessageId}, {Bytes} bytes).",
            routingKey, messageId, body.Length);

        return Task.CompletedTask;
    }

    private IModel EnsureChannel()
    {
        if (_channel is { IsOpen: true } && _exchangeDeclared) return _channel;

        lock (_channelLock)
        {
            if (_channel is { IsOpen: true } && _exchangeDeclared) return _channel;

            _channel?.Dispose();
            _channel = _connections.GetOrCreate().CreateModel();
            _channel.ExchangeDeclare(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: _options.ExchangePersistent,
                autoDelete: false);
            _exchangeDeclared = true;
            return _channel;
        }
    }
}
