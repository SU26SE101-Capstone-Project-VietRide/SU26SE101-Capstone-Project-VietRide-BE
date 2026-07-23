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
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Application.Inbox;
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
    public async Task StartAsync_DeclaresDeadLetterTopologyAndQueueArguments()
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

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
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
        provider.GetRequiredService<IOptions<RabbitMqConsumerOptions<TestIntegrationEvent>>>()
            .Value.Value.QueueName.Should().Be("payment.wallet-bootstrap");
    }

    private static RabbitMqConsumerBackgroundService<TestIntegrationEvent> CreateService(
        IIntegrationEventHandler<TestIntegrationEvent> handler,
        IRabbitMqConnectionFactory? connections = null,
        IIntegrationEventInbox? inbox = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        services.AddScoped(_ => inbox ?? CreateInbox());
        var provider = services.BuildServiceProvider();

        return new RabbitMqConsumerBackgroundService<TestIntegrationEvent>(
            connections ?? Substitute.For<IRabbitMqConnectionFactory>(),
            Options.Create(new RabbitMqOptions()),
            Options.Create(new RabbitMqConsumerOptions<TestIntegrationEvent>
            {
                Value = new RabbitMqConsumerOptions
                {
                    QueueName = "payment.wallet-bootstrap",
                    BindingKeys = new[] { "identity.user.created" },
                },
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<RabbitMqConsumerBackgroundService<TestIntegrationEvent>>>());
    }

    private static BasicDeliverEventArgs CreateDelivery(ulong deliveryTag, TestIntegrationEvent integrationEvent)
    {
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var properties = Substitute.For<IBasicProperties>();
        properties.MessageId.Returns(integrationEvent.EventId.ToString("D"));
        return new BasicDeliverEventArgs(
            "consumer-tag",
            deliveryTag,
            false,
            "vietride.events",
            "identity.user.created",
            properties,
            body);
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
