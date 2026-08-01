using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Messaging.RabbitMq;
using Xunit;

namespace VietRide.Shared.Messaging.UnitTests.RabbitMq;

public sealed class RabbitMqConsumerBackgroundServiceTests
{
    [Fact]
    public async Task ProcessDeliveryAsync_Acks_WhenHandlerSucceeds()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        var inbox = CreateInbox();
        var service = CreateService(handler, inbox: inbox);
        var channel = Substitute.For<IModel>();
        var delivery = CreateDelivery(42, new TestIntegrationEvent { Name = "ok" });

        await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

        await handler.Received(1).HandleAsync(
            Arg.Is<TestIntegrationEvent>(evt => evt.Name == "ok" && evt.EventType == "identity.user.created"),
            Arg.Any<CancellationToken>());
        channel.Received(1).BasicAck(42, multiple: false);
        channel.DidNotReceive().BasicNack(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>());
        await inbox.Received(1).ExecuteAsync(
            "payment.wallet-bootstrap",
            delivery.BasicProperties.MessageId!.AsGuid(),
            Arg.Is<string>(hash => hash.Length == 64),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDeliveryAsync_NacksWithoutRequeue_WhenHandlerFails()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        handler.HandleAsync(Arg.Any<TestIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));

        var service = CreateService(handler, inbox: CreateInbox());
        var channel = Substitute.For<IModel>();
        var delivery = CreateDelivery(43, new TestIntegrationEvent { Name = "fail" });

        await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

        channel.Received(1).BasicNack(43, multiple: false, requeue: false);
        channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ProcessDeliveryAsync_DeadLettersTransientFailure_WhenRetriesAreDisabledByDefault()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        handler.HandleAsync(Arg.Any<TestIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TransientIntegrationEventException("trip unavailable"));
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        var service = CreateService(handler, connections, CreateInbox());
        var channel = Substitute.For<IModel>();

        await service.ProcessDeliveryAsync(
            channel,
            CreateDelivery(44, new TestIntegrationEvent { Name = "default-terminal" }),
            CancellationToken.None);

        channel.Received(1).BasicNack(44, multiple: false, requeue: false);
        channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
        connections.DidNotReceive().GetOrCreate();
    }

    [Fact]
    public async Task ProcessDeliveryAsync_WhenPreCancelled_DoesNotDispatchAcknowledgeOrPublish()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        var service = CreateService(
            handler,
            connections,
            CreateInbox(),
            EnableTransientRetries);
        var channel = Substitute.For<IModel>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => service.ProcessDeliveryAsync(
            channel,
            CreateDelivery(45, new TestIntegrationEvent { Name = "pre-cancelled" }),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await handler.DidNotReceive().HandleAsync(
            Arg.Any<TestIntegrationEvent>(),
            Arg.Any<CancellationToken>());
        channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
        channel.DidNotReceive().BasicNack(
            Arg.Any<ulong>(),
            Arg.Any<bool>(),
            Arg.Any<bool>());
        connections.DidNotReceive().GetOrCreate();
    }

    [Fact]
    public async Task ProcessDeliveryAsync_PublishesFiveDurableRetriesThenDeadLetters_UsingPublishedHeaders()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        handler.HandleAsync(Arg.Any<TestIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TransientIntegrationEventException("trip unavailable"));

        var channel = Substitute.For<IModel>();
        var publishedRetries = new List<(IBasicProperties Properties, ReadOnlyMemory<byte> Body)>();
        var retryPublishers = new List<IModel>();
        var unrelatedProperties = Substitute.For<IBasicProperties>();
        unrelatedProperties.MessageId.Returns(Guid.NewGuid().ToString("D"));
        var connection = Substitute.For<IConnection>();
        connection.CreateModel().Returns(_ =>
        {
            var retryPublisher = Substitute.For<IModel>();
            retryPublisher.CreateBasicProperties()
                .Returns(_ => Substitute.For<IBasicProperties>());
            retryPublisher
                .When(candidate => candidate.BasicPublish(
                    "payment.wallet-bootstrap.retry.dlx",
                    "payment.wallet-bootstrap.retry",
                    true,
                    Arg.Any<IBasicProperties>(),
                    Arg.Any<ReadOnlyMemory<byte>>()))
                .Do(call =>
                {
                    publishedRetries.Add((
                        call.ArgAt<IBasicProperties>(3),
                        call.ArgAt<ReadOnlyMemory<byte>>(4)));
                    if (publishedRetries.Count == 1)
                    {
                        retryPublisher.BasicReturn += Raise.EventWith(new BasicReturnEventArgs
                        {
                            ReplyCode = 312,
                            ReplyText = "NO_ROUTE",
                            Exchange = "another.retry.exchange",
                            RoutingKey = "another.retry.key",
                            BasicProperties = unrelatedProperties,
                            Body = call.ArgAt<ReadOnlyMemory<byte>>(4),
                        });
                    }
                });
            retryPublishers.Add(retryPublisher);
            return retryPublisher;
        });
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var service = CreateService(
            handler,
            connections,
            CreateInbox(),
            EnableTransientRetries);
        var integrationEvent = new TestIntegrationEvent { Name = "transient" };
        var delivery = CreateDelivery(
            100,
            integrationEvent,
            headers: new Dictionary<string, object>
            {
                ["x-death"] = Array.Empty<object>(),
                ["x-first-death-exchange"] = "vietride.events",
                ["x-last-death-queue"] = "payment.wallet-bootstrap.retry",
                ["custom-header"] = "preserved",
            });

        for (var retry = 1; retry <= 5; retry++)
        {
            await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

            publishedRetries.Should().HaveCount(retry);
            var published = publishedRetries[retry - 1];
            published.Properties.MessageId.Should().Be(integrationEvent.EventId.ToString("D"));
            published.Properties.DeliveryMode.Should().Be(2);
            published.Properties.Expiration.Should().Be("10000");
            published.Properties.Headers.Should().Contain("vietride-retry-count", (long)retry);
            published.Properties.Headers.Should().Contain("custom-header", "preserved");
            published.Properties.Headers.Keys.Should().NotContain(key =>
                string.Equals(key, "x-death", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("x-first-death-", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("x-last-death-", StringComparison.OrdinalIgnoreCase));
            channel.Received(1).BasicAck(delivery.DeliveryTag, multiple: false);
            delivery = CreateRedelivery(
                (ulong)(100 + retry),
                published.Properties,
                published.Body);
        }

        await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

        connection.Received(5).CreateModel();
        retryPublishers.Should().HaveCount(5);
        foreach (var retryPublisher in retryPublishers)
        {
            retryPublisher.Received(1).ConfirmSelect();
            retryPublisher.Received(1).BasicPublish(
                "payment.wallet-bootstrap.retry.dlx",
                "payment.wallet-bootstrap.retry",
                true,
                Arg.Any<IBasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>());
            retryPublisher.Received(1).WaitForConfirmsOrDie(Arg.Any<TimeSpan>());
            retryPublisher.Received(1).Dispose();
        }

        channel.Received(1).BasicNack(105, multiple: false, requeue: false);
        channel.DidNotReceive().BasicAck(105, Arg.Any<bool>());
    }

    [Fact]
    public async Task ProcessDeliveryAsync_RequeuesOriginal_WhenRetryPublishIsNotConfirmed()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        handler.HandleAsync(Arg.Any<TestIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TransientIntegrationEventException("trip still unavailable"));

        var channel = Substitute.For<IModel>();
        var retryPublisher = Substitute.For<IModel>();
        var retryProperties = Substitute.For<IBasicProperties>();
        retryPublisher.CreateBasicProperties().Returns(retryProperties);
        retryPublisher.When(candidate => candidate.WaitForConfirmsOrDie(Arg.Any<TimeSpan>()))
            .Do(_ => throw new InvalidOperationException("publisher confirm timed out"));
        var connection = Substitute.For<IConnection>();
        connection.CreateModel().Returns(retryPublisher);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var service = CreateService(
            handler,
            connections,
            CreateInbox(),
            EnableTransientRetries);
        var delivery = CreateDelivery(
            48,
            new TestIntegrationEvent { Name = "transient-confirm-failure" });

        await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

        channel.Received(1).BasicNack(48, multiple: false, requeue: true);
        channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
        retryPublisher.Received(1).BasicPublish(
            "payment.wallet-bootstrap.retry.dlx",
            "payment.wallet-bootstrap.retry",
            true,
            retryProperties,
            Arg.Any<ReadOnlyMemory<byte>>());
        retryPublisher.Received(1).Dispose();
        channel.DidNotReceive().Dispose();
        retryProperties.Headers.Should().Contain("vietride-retry-count", 1L);
        retryProperties.Expiration.Should().Be("10000");
        (delivery.BasicProperties.Headers?.ContainsKey("vietride-retry-count") ?? false)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("not-an-integer")]
    [InlineData(-1)]
    public async Task ProcessDeliveryAsync_DeadLettersTransientFailure_WhenRetryHeaderIsInvalid(
        object retryHeader)
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        handler.HandleAsync(Arg.Any<TestIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TransientIntegrationEventException("trip still unavailable"));

        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        var service = CreateService(
            handler,
            connections,
            CreateInbox(),
            EnableTransientRetries);
        var channel = Substitute.For<IModel>();
        var delivery = CreateDelivery(
            49,
            new TestIntegrationEvent { Name = "transient-malformed-header" },
            headers: new Dictionary<string, object>
            {
                ["vietride-retry-count"] = retryHeader,
            });

        await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

        channel.Received(1).BasicNack(49, multiple: false, requeue: false);
        connections.DidNotReceive().GetOrCreate();
        channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ProcessDeliveryAsync_RequeuesOriginal_WhenConfirmedRetryPublishIsReturned()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        handler.HandleAsync(Arg.Any<TestIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TransientIntegrationEventException("trip still unavailable"));

        var channel = Substitute.For<IModel>();
        var retryPublisher = Substitute.For<IModel>();
        var retryProperties = Substitute.For<IBasicProperties>();
        retryPublisher.CreateBasicProperties().Returns(retryProperties);
        retryPublisher
            .When(candidate => candidate.BasicPublish(
                "payment.wallet-bootstrap.retry.dlx",
                "payment.wallet-bootstrap.retry",
                true,
                retryProperties,
                Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(call =>
            {
                retryPublisher.BasicReturn += Raise.EventWith(new BasicReturnEventArgs
                {
                    ReplyCode = 312,
                    ReplyText = "NO_ROUTE",
                    Exchange = "payment.wallet-bootstrap.retry.dlx",
                    RoutingKey = "payment.wallet-bootstrap.retry",
                    BasicProperties = retryProperties,
                    Body = call.ArgAt<ReadOnlyMemory<byte>>(4),
                });
            });
        var connection = Substitute.For<IConnection>();
        connection.CreateModel().Returns(retryPublisher);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var service = CreateService(
            handler,
            connections,
            CreateInbox(),
            EnableTransientRetries);
        var delivery = CreateDelivery(
            49,
            new TestIntegrationEvent { Name = "transient-unroutable" });

        await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

        Received.InOrder(() =>
        {
            retryPublisher.ConfirmSelect();
            retryPublisher.BasicPublish(
                "payment.wallet-bootstrap.retry.dlx",
                "payment.wallet-bootstrap.retry",
                true,
                retryProperties,
                Arg.Any<ReadOnlyMemory<byte>>());
            retryPublisher.WaitForConfirmsOrDie(Arg.Any<TimeSpan>());
            channel.BasicNack(49, multiple: false, requeue: true);
        });
        retryPublisher.Received(1).Dispose();
        channel.DidNotReceive().Dispose();
        channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task StartAsync_DeclaresDeadLetterAndTransientRetryTopology()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IModel>();
        connection.CreateModel().Returns(channel);
        channel.IsOpen.Returns(true);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var service = CreateService(
            handler,
            connections,
            CreateInbox(),
            EnableTransientRetries);

        await service.StartAsync(CancellationToken.None);

        channel.Received(1).ExchangeDeclare(
            "vietride.events",
            ExchangeType.Topic,
            true,
            false,
            null);
        channel.Received(1).ExchangeDeclare(
            "payment.wallet-bootstrap.dlx",
            ExchangeType.Direct,
            true,
            false,
            null);
        channel.Received(1).QueueDeclare(
            "payment.wallet-bootstrap.dlq",
            true,
            false,
            false,
            null);
        channel.Received(1).QueueBind(
            "payment.wallet-bootstrap.dlq",
            "payment.wallet-bootstrap.dlx",
            "payment.wallet-bootstrap.dead",
            null);
        channel.Received(1).QueueDeclare(
            "payment.wallet-bootstrap",
            true,
            false,
            false,
            Arg.Is<IDictionary<string, object>>(arguments =>
                (string)arguments["x-dead-letter-exchange"] == "payment.wallet-bootstrap.dlx"
                && (string)arguments["x-dead-letter-routing-key"] == "payment.wallet-bootstrap.dead"));
        channel.Received(1).ExchangeDeclare(
            "payment.wallet-bootstrap.retry.dlx",
            ExchangeType.Direct,
            true,
            false,
            null);
        channel.Received(1).QueueDeclare(
            "payment.wallet-bootstrap.retry",
            true,
            false,
            false,
            Arg.Is<IDictionary<string, object>>(arguments =>
                !arguments.ContainsKey("x-message-ttl")
                && (string)arguments["x-dead-letter-exchange"] == "vietride.events"
                && (string)arguments["x-dead-letter-routing-key"]
                    == "__retry__.payment.wallet-bootstrap"));
        channel.Received(1).QueueBind(
            "payment.wallet-bootstrap.retry",
            "payment.wallet-bootstrap.retry.dlx",
            "payment.wallet-bootstrap.retry",
            null);
        channel.Received(1).QueueBind(
            "payment.wallet-bootstrap",
            "vietride.events",
            "__retry__.payment.wallet-bootstrap",
            null);

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [Fact]
    public async Task StartAsync_DefaultRetriesDisabled_DoesNotDeclareRetryTopology()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IModel>();
        connection.CreateModel().Returns(channel);
        channel.IsOpen.Returns(true);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var service = CreateService(handler, connections, CreateInbox());

        await service.StartAsync(CancellationToken.None);

        channel.DidNotReceive().ExchangeDeclare(
            "payment.wallet-bootstrap.retry.dlx",
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IDictionary<string, object>>());
        channel.DidNotReceive().QueueDeclare(
            "payment.wallet-bootstrap.retry",
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IDictionary<string, object>>());
        channel.DidNotReceive().QueueBind(
            "payment.wallet-bootstrap",
            "vietride.events",
            "__retry__.payment.wallet-bootstrap",
            Arg.Any<IDictionary<string, object>>());

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [Fact]
    public async Task StartAsync_RedeclaresSameRetryTopology_WhenDelayChanges()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        var connection = Substitute.For<IConnection>();
        var firstChannel = Substitute.For<IModel>();
        var secondChannel = Substitute.For<IModel>();
        firstChannel.IsOpen.Returns(true);
        secondChannel.IsOpen.Returns(true);
        connection.CreateModel().Returns(firstChannel, secondChannel);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var firstService = CreateService(
            handler,
            connections,
            CreateInbox(),
            options =>
            {
                options.TransientRetryCount = 5;
                options.TransientRetryDelay = TimeSpan.FromSeconds(10);
            });
        var secondService = CreateService(
            handler,
            connections,
            CreateInbox(),
            options =>
            {
                options.TransientRetryCount = 5;
                options.TransientRetryDelay = TimeSpan.FromSeconds(30);
            });

        await firstService.StartAsync(CancellationToken.None);
        await secondService.StartAsync(CancellationToken.None);

        firstChannel.Received(1).QueueDeclare(
            "payment.wallet-bootstrap.retry",
            true,
            false,
            false,
            Arg.Is<IDictionary<string, object>>(arguments =>
                !arguments.ContainsKey("x-message-ttl")
                && (string)arguments["x-dead-letter-exchange"] == "vietride.events"
                && (string)arguments["x-dead-letter-routing-key"]
                    == "__retry__.payment.wallet-bootstrap"));
        secondChannel.Received(1).QueueDeclare(
            "payment.wallet-bootstrap.retry",
            true,
            false,
            false,
            Arg.Is<IDictionary<string, object>>(arguments =>
                !arguments.ContainsKey("x-message-ttl")
                && (string)arguments["x-dead-letter-exchange"] == "vietride.events"
                && (string)arguments["x-dead-letter-routing-key"]
                    == "__retry__.payment.wallet-bootstrap"));

        await firstService.StopAsync(CancellationToken.None);
        await secondService.StopAsync(CancellationToken.None);
        firstService.Dispose();
        secondService.Dispose();
    }

    [Fact]
    public void AddVietRideEventConsumer_RegistersHandlerAndHostedServiceOptions()
    {
        var services = new ServiceCollection();

        services.AddVietRideEventConsumer<TestIntegrationEvent, TestIntegrationEventHandler>(options =>
        {
            options.QueueName = "payment.wallet-bootstrap";
            options.BindingKeys = new[] { "identity.user.created" };
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IIntegrationEventHandler<TestIntegrationEvent>>()
            .Should().BeOfType<TestIntegrationEventHandler>();
        var options = provider
            .GetRequiredService<IOptions<RabbitMqConsumerOptions<TestIntegrationEvent>>>()
            .Value.Value;
        options.QueueName.Should().Be("payment.wallet-bootstrap");
        options.TransientRetryCount.Should().Be(0);
    }

    private static RabbitMqConsumerBackgroundService<TestIntegrationEvent> CreateService(
        IIntegrationEventHandler<TestIntegrationEvent> handler,
        IRabbitMqConnectionFactory? connections = null,
        IIntegrationEventInbox? inbox = null,
        Action<RabbitMqConsumerOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        services.AddScoped(_ => inbox ?? CreateInbox());
        var provider = services.BuildServiceProvider();
        var consumerOptions = new RabbitMqConsumerOptions
        {
            QueueName = "payment.wallet-bootstrap",
            BindingKeys = new[] { "identity.user.created" },
        };
        configureOptions?.Invoke(consumerOptions);

        return new RabbitMqConsumerBackgroundService<TestIntegrationEvent>(
            connections ?? Substitute.For<IRabbitMqConnectionFactory>(),
            Options.Create(new RabbitMqOptions()),
            Options.Create(new RabbitMqConsumerOptions<TestIntegrationEvent>
            {
                Value = consumerOptions,
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<RabbitMqConsumerBackgroundService<TestIntegrationEvent>>>());
    }

    private static BasicDeliverEventArgs CreateDelivery(
        ulong deliveryTag,
        TestIntegrationEvent integrationEvent,
        IDictionary<string, object>? headers = null)
    {
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var properties = Substitute.For<IBasicProperties>();
        properties.MessageId.Returns(integrationEvent.EventId.ToString("D"));
        if (headers is not null)
        {
            properties.Headers.Returns(headers);
        }

        return new BasicDeliverEventArgs(
            "consumer-tag",
            deliveryTag,
            redelivered: headers is not null,
            "vietride.events",
            "identity.user.created",
            properties,
            body);
    }

    private static BasicDeliverEventArgs CreateRedelivery(
        ulong deliveryTag,
        IBasicProperties properties,
        ReadOnlyMemory<byte> body)
        => new(
            "consumer-tag",
            deliveryTag,
            redelivered: true,
            "vietride.events",
            "__retry__.payment.wallet-bootstrap",
            properties,
            body);

    private static void EnableTransientRetries(RabbitMqConsumerOptions options)
    {
        options.TransientRetryCount = 5;
        options.TransientRetryDelay = TimeSpan.FromSeconds(10);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_AcksDuplicate_WithoutCallingHandler()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(IntegrationEventInboxResult.Duplicate);
        var service = CreateService(handler, inbox: inbox);
        var channel = Substitute.For<IModel>();

        await service.ProcessDeliveryAsync(
            channel,
            CreateDelivery(44, new TestIntegrationEvent { Name = "duplicate" }),
            CancellationToken.None);

        await handler.DidNotReceive().HandleAsync(
            Arg.Any<TestIntegrationEvent>(),
            Arg.Any<CancellationToken>());
        channel.Received(1).BasicAck(44, multiple: false);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_NacksPayloadMismatch()
    {
        var handler = Substitute.For<IIntegrationEventHandler<TestIntegrationEvent>>();
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IntegrationEventInboxResult>(
                new IntegrationEventPayloadMismatchException(
                    "payment.wallet-bootstrap",
                    Guid.NewGuid())));
        var service = CreateService(handler, inbox: inbox);
        var channel = Substitute.For<IModel>();

        await service.ProcessDeliveryAsync(
            channel,
            CreateDelivery(45, new TestIntegrationEvent { Name = "mismatch" }),
            CancellationToken.None);

        channel.Received(1).BasicNack(45, multiple: false, requeue: false);
        channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ProcessDeliveryAsync_UsesCanonicalPayloadIdentity_ForLegacyConsumerMirror()
    {
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var handler = Substitute.For<IIntegrationEventHandler<LegacyDerivedIntegrationEvent>>();
        var service = CreateService(handler, CreateInbox());
        var channel = Substitute.For<IModel>();
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new { eventId, occurredAt = DateTime.UtcNow, userId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var properties = Substitute.For<IBasicProperties>();
        properties.MessageId.Returns(eventId.ToString("D"));
        var delivery = new BasicDeliverEventArgs(
            "consumer-tag",
            46,
            false,
            "vietride.events",
            "identity.user.created",
            properties,
            payload);

        await service.ProcessDeliveryAsync(channel, delivery, CancellationToken.None);

        await handler.Received(1).HandleAsync(
            Arg.Is<LegacyDerivedIntegrationEvent>(evt => evt.UserId == userId),
            Arg.Any<CancellationToken>());
        channel.Received(1).BasicAck(46, multiple: false);
        channel.DidNotReceive().BasicNack(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    private static RabbitMqConsumerBackgroundService<TEvent> CreateService<TEvent>(
        IIntegrationEventHandler<TEvent> handler,
        IIntegrationEventInbox inbox)
        where TEvent : IIntegrationEvent
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        services.AddScoped(_ => inbox);
        var provider = services.BuildServiceProvider();

        return new RabbitMqConsumerBackgroundService<TEvent>(
            Substitute.For<IRabbitMqConnectionFactory>(),
            Options.Create(new RabbitMqOptions()),
            Options.Create(new RabbitMqConsumerOptions<TEvent>
            {
                Value = new RabbitMqConsumerOptions
                {
                    QueueName = "payment.wallet-bootstrap",
                    BindingKeys = new[] { "identity.user.created" },
                },
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<RabbitMqConsumerBackgroundService<TEvent>>>());
    }

    private static IIntegrationEventInbox CreateInbox()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await call.ArgAt<Func<CancellationToken, Task>>(3)(
                    call.ArgAt<CancellationToken>(4));
                return IntegrationEventInboxResult.Processed;
            });
        return inbox;
    }

    public sealed class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class TestIntegrationEvent : IIntegrationEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string EventType { get; set; } = "identity.user.created";
        public string Name { get; set; } = string.Empty;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class LegacyDerivedIntegrationEvent : IIntegrationEvent
    {
        public Guid UserId { get; set; }

        [JsonIgnore]
        public Guid EventId => UserId;

        [JsonIgnore]
        public DateTime OccurredAt => DateTime.UtcNow;

        [JsonIgnore]
        public string EventType => "identity.user.created";
    }
}

internal static class GuidTestExtensions
{
    public static Guid AsGuid(this string value) => Guid.Parse(value);
}
