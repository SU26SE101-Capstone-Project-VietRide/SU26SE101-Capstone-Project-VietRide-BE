using System.Globalization;
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
/// An explicitly configured <see cref="TransientIntegrationEventException"/> is copied to a
/// durable TTL retry queue and acknowledged only after publisher confirmation. Exhausted
/// transient failures and all other exceptions reject without requeue for terminal DLQ handling.
/// </remarks>
public sealed class RabbitMqConsumerBackgroundService<TEvent> : BackgroundService
    where TEvent : IIntegrationEvent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int ConnectRetryDelaySeconds = 5;
    private const string RetryCountHeader = "vietride-retry-count";
    private static readonly TimeSpan RetryPublishConfirmTimeout = TimeSpan.FromSeconds(5);

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
        DeclareTransientRetryTopology();

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

    private void DeclareTransientRetryTopology()
    {
        if (_consumerOptions.TransientRetryCount == 0)
        {
            return;
        }

        var channel = _channel!;
        channel.ExchangeDeclare(
            exchange: RetryExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        var retryQueueArguments = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = _rabbitMqOptions.ExchangeName,
            ["x-dead-letter-routing-key"] = RetryReturnRoutingKey,
        };
        channel.QueueDeclare(
            queue: RetryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: retryQueueArguments);
        channel.QueueBind(
            queue: RetryQueueName,
            exchange: RetryExchangeName,
            routingKey: RetryPublishRoutingKey);
        channel.QueueBind(
            queue: _consumerOptions.QueueName,
            exchange: _rabbitMqOptions.ExchangeName,
            routingKey: RetryReturnRoutingKey);
    }

    private static bool IsPreconditionFailed(OperationInterruptedException ex)
        => ex.ShutdownReason?.ReplyCode == 406;

    /// <summary>
    /// Dispatches a single delivery to the registered handler and acknowledges
    /// or rejects it. Exposed for unit tests; production calls it from RabbitMQ.
    /// </summary>
    public async Task ProcessDeliveryAsync(IModel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        catch (TransientIntegrationEventException ex)
            when (_consumerOptions.TransientRetryCount > 0)
        {
            HandleTransientFailure(channel, args, ex, cancellationToken);
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

    private void HandleTransientFailure(
        IModel channel,
        BasicDeliverEventArgs args,
        TransientIntegrationEventException exception,
        CancellationToken cancellationToken)
    {
        var retryCount = GetTransientRetryCount(args.BasicProperties);
        if (retryCount >= _consumerOptions.TransientRetryCount)
        {
            channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
            _logger.LogWarning(
                exception,
                "RabbitMQ transient delivery exhausted {RetryCount} delayed retries and was dead-lettered: queue={Queue} routingKey={RoutingKey} tag={DeliveryTag}.",
                retryCount,
                _consumerOptions.QueueName,
                args.RoutingKey,
                args.DeliveryTag);
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var retryPublisherChannel = _connections.GetOrCreate().CreateModel();
            var retryProperties = CloneForPersistentRetry(
                retryPublisherChannel,
                args.BasicProperties,
                retryCount + 1,
                _consumerOptions.TransientRetryDelay);
            var retryWasReturned = 0;
            EventHandler<BasicReturnEventArgs> returnedHandler = (_, returned) =>
            {
                if (string.Equals(
                    returned.BasicProperties.MessageId,
                    retryProperties.MessageId,
                    StringComparison.Ordinal))
                {
                    Interlocked.Exchange(ref retryWasReturned, 1);
                }
            };

            retryPublisherChannel.BasicReturn += returnedHandler;
            try
            {
                retryPublisherChannel.ConfirmSelect();
                retryPublisherChannel.BasicPublish(
                    exchange: RetryExchangeName,
                    routingKey: RetryPublishRoutingKey,
                    mandatory: true,
                    basicProperties: retryProperties,
                    body: args.Body);
                retryPublisherChannel.WaitForConfirmsOrDie(RetryPublishConfirmTimeout);
                if (Volatile.Read(ref retryWasReturned) != 0)
                {
                    throw new InvalidOperationException(
                        $"RabbitMQ returned delayed-retry message {retryProperties.MessageId} as unroutable.");
                }

                channel.BasicAck(args.DeliveryTag, multiple: false);
                _logger.LogWarning(
                    exception,
                    "RabbitMQ transient delivery scheduled for delayed retry {NextRetry}/{MaxRetries}: queue={Queue} routingKey={RoutingKey} tag={DeliveryTag} delay={RetryDelay}.",
                    retryCount + 1,
                    _consumerOptions.TransientRetryCount,
                    _consumerOptions.QueueName,
                    args.RoutingKey,
                    args.DeliveryTag,
                    _consumerOptions.TransientRetryDelay);
            }
            finally
            {
                retryPublisherChannel.BasicReturn -= returnedHandler;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception publishException)
        {
            channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
            _logger.LogError(
                publishException,
                "RabbitMQ delayed-retry publish was not safely routed and confirmed; original delivery was requeued: queue={Queue} routingKey={RoutingKey} tag={DeliveryTag}.",
                _consumerOptions.QueueName,
                args.RoutingKey,
                args.DeliveryTag);
        }
    }

    private long GetTransientRetryCount(IBasicProperties basicProperties)
    {
        if (basicProperties.Headers is null
            || !basicProperties.Headers.TryGetValue(RetryCountHeader, out var rawRetryCount))
        {
            return 0;
        }

        var retryCount = rawRetryCount switch
        {
            int value when value >= 0 => value,
            long value when value >= 0 => value,
            _ => -1,
        };
        if (retryCount >= 0)
        {
            return retryCount;
        }

        _logger.LogWarning(
            "RabbitMQ retry-count header was malformed for queue {Queue}; exhausting the delivery to prevent an unbounded retry loop.",
            _consumerOptions.QueueName);
        return long.MaxValue;
    }

    private static IBasicProperties CloneForPersistentRetry(
        IModel channel,
        IBasicProperties source,
        long nextRetryCount,
        TimeSpan retryDelay)
    {
        var clone = channel.CreateBasicProperties();
        clone.AppId = source.AppId;
        clone.ClusterId = source.ClusterId;
        clone.ContentEncoding = source.ContentEncoding;
        clone.ContentType = source.ContentType;
        clone.CorrelationId = source.CorrelationId;
        clone.DeliveryMode = 2;
        clone.Expiration = ((long)Math.Ceiling(retryDelay.TotalMilliseconds))
            .ToString(CultureInfo.InvariantCulture);
        clone.Headers = source.Headers?
            .Where(header => !IsBrokerDeathHeader(header.Key))
            .ToDictionary(header => header.Key, header => header.Value)
            ?? new Dictionary<string, object>();
        clone.Headers[RetryCountHeader] = nextRetryCount;
        clone.MessageId = source.MessageId;
        clone.Priority = source.Priority;
        clone.ReplyTo = source.ReplyTo;
        clone.Timestamp = source.Timestamp;
        clone.Type = source.Type;
        clone.UserId = source.UserId;
        return clone;
    }

    private static bool IsBrokerDeathHeader(string headerName)
        => string.Equals(headerName, "x-death", StringComparison.OrdinalIgnoreCase)
            || headerName.StartsWith("x-first-death-", StringComparison.OrdinalIgnoreCase)
            || headerName.StartsWith("x-last-death-", StringComparison.OrdinalIgnoreCase);

    private string RetryExchangeName => $"{_consumerOptions.QueueName}.retry.dlx";
    private string RetryQueueName => $"{_consumerOptions.QueueName}.retry";
    private string RetryPublishRoutingKey => $"{_consumerOptions.QueueName}.retry";
    private string RetryReturnRoutingKey => $"__retry__.{_consumerOptions.QueueName}";

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
