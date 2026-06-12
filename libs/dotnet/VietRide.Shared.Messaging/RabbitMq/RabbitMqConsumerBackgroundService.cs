using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Shared.Messaging.RabbitMq;

/// <summary>
/// Consumes one integration-event type from RabbitMQ topic exchange
/// <c>vietride.events</c> using a durable named queue and manual acknowledgements.
/// </summary>
/// <remarks>
/// Delivery is at-least-once: the broker can re-deliver a message after process
/// failure before acknowledgement, so registered handlers MUST be idempotent.
/// Handler exceptions reject the message with <c>BasicNack(requeue: false)</c>
/// to avoid infinite requeue loops and allow broker dead-letter handling.
/// </remarks>
public sealed class RabbitMqConsumerBackgroundService<TEvent> : BackgroundService
    where TEvent : IIntegrationEvent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRabbitMqConnectionFactory _connections;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly RabbitMqConsumerOptions _consumerOptions;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RabbitMqConsumerBackgroundService<TEvent>> _logger;

    private IModel? _channel;

    public RabbitMqConsumerBackgroundService(
        IRabbitMqConnectionFactory connections,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        IOptions<RabbitMqConsumerOptions<TEvent>> consumerOptions,
        IServiceScopeFactory scopes,
        ILogger<RabbitMqConsumerBackgroundService<TEvent>> logger)
    {
        _connections = connections;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _consumerOptions = consumerOptions.Value.Value;
        _scopes = scopes;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumerOptions.Validate();

        _channel = _connections.GetOrCreate().CreateModel();
        _channel.ExchangeDeclare(
            exchange: _rabbitMqOptions.ExchangeName,
            type: ExchangeType.Topic,
            durable: _rabbitMqOptions.ExchangePersistent,
            autoDelete: false);
        _channel.QueueDeclare(
            queue: _consumerOptions.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        foreach (var bindingKey in _consumerOptions.BindingKeys)
        {
            _channel.QueueBind(
                queue: _consumerOptions.QueueName,
                exchange: _rabbitMqOptions.ExchangeName,
                routingKey: bindingKey);
        }

        _channel.BasicQos(prefetchSize: 0, prefetchCount: _consumerOptions.PrefetchCount, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += (_, args) => ProcessDeliveryAsync(_channel, args, stoppingToken);

        _channel.BasicConsume(
            queue: _consumerOptions.QueueName,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation(
            "RabbitMQ consumer started: queue={Queue} bindings={Bindings}.",
            _consumerOptions.QueueName,
            string.Join(",", _consumerOptions.BindingKeys));

        return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    /// <summary>
    /// Dispatches a single delivery to the registered handler and acknowledges
    /// or rejects it. Exposed for unit tests; production calls it from RabbitMQ.
    /// </summary>
    public async Task ProcessDeliveryAsync(IModel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        try
        {
            var integrationEvent = JsonSerializer.Deserialize<TEvent>(args.Body.Span, JsonOptions)
                ?? throw new JsonException($"RabbitMQ delivery {args.DeliveryTag} deserialized to null.");

            using var scope = _scopes.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
            await handler.HandleAsync(integrationEvent, cancellationToken).ConfigureAwait(false);

            channel.BasicAck(args.DeliveryTag, multiple: false);
            _logger.LogDebug(
                "RabbitMQ delivery acked: queue={Queue} routingKey={RoutingKey} tag={DeliveryTag}.",
                _consumerOptions.QueueName,
                args.RoutingKey,
                args.DeliveryTag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
            _logger.LogWarning(
                ex,
                "RabbitMQ delivery nacked without requeue: queue={Queue} routingKey={RoutingKey} tag={DeliveryTag}.",
                _consumerOptions.QueueName,
                args.RoutingKey,
                args.DeliveryTag);
        }
    }

    public override void Dispose()
    {
        try
        {
            if (_channel is { IsOpen: true }) _channel.Close();
            _channel?.Dispose();
        }
        finally
        {
            base.Dispose();
        }
    }
}
