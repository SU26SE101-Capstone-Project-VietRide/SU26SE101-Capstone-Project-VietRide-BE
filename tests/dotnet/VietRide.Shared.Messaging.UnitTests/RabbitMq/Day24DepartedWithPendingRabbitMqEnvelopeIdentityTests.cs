using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using VietRide.Shared.Messaging.RabbitMq;
using Xunit;

namespace VietRide.Shared.Messaging.UnitTests.RabbitMq;

public sealed class Day24DepartedWithPendingRabbitMqEnvelopeIdentityTests
{
    [Fact]
    public async Task PublishRaw_UsesCanonicalEnvelopeAndRestartDeliversSameUnprocessedIdentity()
    {
        const string routingKey = "trip.stop.departed_with_pending";
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId, eventType = routingKey });
        var properties = Substitute.For<IBasicProperties>();
        var channel = Substitute.For<IModel>();
        channel.IsOpen.Returns(true);
        channel.CreateBasicProperties().Returns(properties);
        string? exchange = null;
        string? publishedRoutingKey = null;
        ReadOnlyMemory<byte> body = default;
        channel.When(model => model.BasicPublish(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<IBasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(call =>
            {
                exchange = call.ArgAt<string>(0);
                publishedRoutingKey = call.ArgAt<string>(1);
                body = call.ArgAt<ReadOnlyMemory<byte>>(4);
            });
        var connection = Substitute.For<IConnection>();
        connection.CreateModel().Returns(channel);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var publisher = new RabbitMqEventPublisher(
            connections,
            Options.Create(new RabbitMqOptions()),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());

        await publisher.PublishRawAsync(routingKey, eventId, payload, CancellationToken.None);
        var restartedPublisher = new RabbitMqEventPublisher(
            connections,
            Options.Create(new RabbitMqOptions()),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());
        await restartedPublisher.PublishRawAsync(
            routingKey,
            eventId,
            payload,
            CancellationToken.None);

        channel.Received(2).ExchangeDeclare(
            "vietride.events",
            ExchangeType.Topic,
            true,
            false,
            null);
        exchange.Should().Be("vietride.events");
        publishedRoutingKey.Should().Be(routingKey);
        properties.MessageId.Should().Be(eventId.ToString("D"));
        properties.Type.Should().Be(routingKey);
        properties.DeliveryMode.Should().Be(2);
        Encoding.UTF8.GetString(body.Span).Should().Be(payload);
    }
}
