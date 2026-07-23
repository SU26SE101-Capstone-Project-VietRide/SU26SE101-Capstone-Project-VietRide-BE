using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using VietRide.Shared.Application.Inbox;
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

    private const int ConnectRetryDelaySeconds = 5;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumerOptions.Validate();

        // Resilient startup: a broker that is briefly unreachable at boot must NOT crash the host
        // (BackgroundService default is StopHost on an unhandled exception). Retry the channel setup
        // until it succeeds or the host is stopping.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                StartConsuming(stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "RabbitMQ consumer setup failed for queue {Queue}; retrying in {Delay}s.",
                    _consumerOptions.QueueName,
                    ConnectRetryDelaySeconds);

                try { _channel?.Dispose(); }
                catch { /* best-effort cleanup before retry */ }
                _channel = null;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(ConnectRetryDelaySeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void StartConsuming(CancellationToken stoppingToken)
    {
        _channel = _connections.GetOrCreate().CreateModel();
        _channel.ExchangeDeclare(
            exchange: _rabbitMqOptions.ExchangeName,
            type: ExchangeType.Topic,
            durable: _rabbitMqOptions.ExchangePersistent,
            autoDelete: false);

        _channel.ExchangeDeclare(
            exchange: _consumerOptions.ResolvedDeadLetterExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: _consumerOptions.ResolvedDeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        _channel.QueueBind(
            queue: _consumerOptions.ResolvedDeadLetterQueueName,
            exchange: _consumerOptions.ResolvedDeadLetterExchangeName,
            routingKey: _consumerOptions.ResolvedDeadLetterRoutingKey);

        var queueArguments = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = _consumerOptions.ResolvedDeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = _consumerOptions.ResolvedDeadLetterRoutingKey,
        };

        DeclareSourceQueueWithDeadLetterArguments(queueArguments);

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
    }

    private void DeclareSourceQueueWithDeadLetterArguments(IDictionary<string, object> queueArguments)
    {
        try
        {
            DeclareSourceQueue(queueArguments);
        }
        catch (OperationInterruptedException ex) when (IsPreconditionFailed(ex))
        {
            _logger.LogWarning(
                ex,
                "RabbitMQ queue {Queue} exists with incompatible arguments; deleting it once to apply dead-letter topology.",
                _consumerOptions.QueueName);

            using var adminChannel = _connections.GetOrCreate().CreateModel();
            adminChannel.QueueDelete(_consumerOptions.QueueName, ifUnused: false, ifEmpty: false);

            _channel = _connections.GetOrCreate().CreateModel();
            _channel.ExchangeDeclare(
                exchange: _rabbitMqOptions.ExchangeName,
                type: ExchangeType.Topic,
                durable: _rabbitMqOptions.ExchangePersistent,
                autoDelete: false);
            _channel.ExchangeDeclare(
                exchange: _consumerOptions.ResolvedDeadLetterExchangeName,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false);
            _channel.QueueDeclare(
                queue: _consumerOptions.ResolvedDeadLetterQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);
            _channel.QueueBind(
                queue: _consumerOptions.ResolvedDeadLetterQueueName,
                exchange: _consumerOptions.ResolvedDeadLetterExchangeName,
                routingKey: _consumerOptions.ResolvedDeadLetterRoutingKey);
            DeclareSourceQueue(queueArguments);
        }
    }

    private void DeclareSourceQueue(IDictionary<string, object> queueArguments)
    {
        _channel!.QueueDeclare(
            queue: _consumerOptions.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments);
    }

    private static bool IsPreconditionFailed(OperationInterruptedException ex)
        => ex.ShutdownReason?.ReplyCode == 406;

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

            var payloadEventId = ResolvePayloadEventId(args.Body, integrationEvent.EventId);
            if (!Guid.TryParse(args.BasicProperties.MessageId, out var messageId)
                || messageId == Guid.Empty
                || messageId != payloadEventId)
            {
                throw new JsonException(
                    $"RabbitMQ delivery {args.DeliveryTag} has a missing or inconsistent MessageId.");
            }

            var payloadHash = Convert.ToHexString(SHA256.HashData(args.Body.Span));

            using var scope = _scopes.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
            var inbox = scope.ServiceProvider.GetRequiredService<IIntegrationEventInbox>();
            var inboxResult = await inbox.ExecuteAsync(
                _consumerOptions.QueueName,
                messageId,
                payloadHash,
                ct => handler.HandleAsync(integrationEvent, ct),
                cancellationToken).ConfigureAwait(false);

            channel.BasicAck(args.DeliveryTag, multiple: false);
            _logger.LogDebug(
                "RabbitMQ delivery acked: queue={Queue} routingKey={RoutingKey} tag={DeliveryTag} inbox={InboxResult}.",
                _consumerOptions.QueueName,
                args.RoutingKey,
                args.DeliveryTag,
                inboxResult);
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

    private static Guid ResolvePayloadEventId(
        ReadOnlyMemory<byte> payload,
        Guid legacyEventId)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("RabbitMQ integration-event payload must be a JSON object.");
        }

        var eventIdProperties = document.RootElement
            .EnumerateObject()
            .Where(property => string.Equals(
                property.Name,
                "eventId",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (eventIdProperties.Length == 0)
        {
            return legacyEventId;
        }

        if (eventIdProperties.Length > 1
            || !eventIdProperties[0].Value.TryGetGuid(out var payloadEventId)
            || payloadEventId == Guid.Empty)
        {
            throw new JsonException("RabbitMQ integration-event payload has an invalid eventId.");
        }

        return payloadEventId;
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
