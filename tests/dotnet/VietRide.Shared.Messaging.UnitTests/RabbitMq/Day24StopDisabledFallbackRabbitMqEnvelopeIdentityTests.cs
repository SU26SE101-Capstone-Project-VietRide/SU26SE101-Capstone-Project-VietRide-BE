using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using VietRide.Shared.Messaging.RabbitMq;
using Xunit;

namespace VietRide.Shared.Messaging.UnitTests.RabbitMq;

public sealed class Day24StopDisabledFallbackRabbitMqEnvelopeIdentityTests
{
    [Fact]
    public async Task PublisherRestart_UsesTopicExchangeRoutingKeyAndSameOutboxIdentity()
    {
        var eventId = Guid.NewGuid();
        const string routingKey = "booking.booking.stop_disabled_auto_fallback_applied";
        var payload = $"{{\"eventId\":\"{eventId:D}\"}}";
        var firstProperties = Substitute.For<IBasicProperties>();
        var secondProperties = Substitute.For<IBasicProperties>();
        var firstChannel = Substitute.For<IModel>();
        var secondChannel = Substitute.For<IModel>();
        firstChannel.IsOpen.Returns(true);
        secondChannel.IsOpen.Returns(true);
        firstChannel.CreateBasicProperties().Returns(firstProperties);
        secondChannel.CreateBasicProperties().Returns(secondProperties);
        var firstConnection = Substitute.For<IConnection>();
        var secondConnection = Substitute.For<IConnection>();
        firstConnection.CreateModel().Returns(firstChannel);
        secondConnection.CreateModel().Returns(secondChannel);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(firstConnection, secondConnection);

        RabbitMqEventPublisher CreatePublisher() => new(
            connections,
            Options.Create(new RabbitMqOptions()),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());

        firstChannel.When(channel => channel.BasicPublish(
                "vietride.events", routingKey, false, firstProperties, Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(_ => throw new IOException("simulated broker failure"));

        var firstAttempt = () => CreatePublisher().PublishRawAsync(
            routingKey, eventId, payload, CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<IOException>();
        firstProperties.MessageId.Should().Be(eventId.ToString("D"));
        firstProperties.Type.Should().Be(routingKey);

        await CreatePublisher().PublishRawAsync(routingKey, eventId, payload, CancellationToken.None);

        secondChannel.Received(1).ExchangeDeclare("vietride.events", ExchangeType.Topic, true, false, null);
        secondChannel.Received(1).BasicPublish(
            "vietride.events",
            routingKey,
            false,
            Arg.Is<IBasicProperties>(properties =>
                properties.MessageId == eventId.ToString("D") && properties.Type == routingKey),
            Arg.Is<ReadOnlyMemory<byte>>(body =>
                body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(payload))));
    }
}
