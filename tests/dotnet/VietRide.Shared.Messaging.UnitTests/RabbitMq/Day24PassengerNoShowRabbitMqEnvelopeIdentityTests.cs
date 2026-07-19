using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using VietRide.Shared.Messaging.RabbitMq;
using Xunit;

namespace VietRide.Shared.Messaging.UnitTests.RabbitMq;

public sealed class Day24PassengerNoShowRabbitMqEnvelopeIdentityTests
{
    [Fact]
    public async Task PublisherRestartUsesTopicExchangeRoutingKeyAndSameOutboxIdentity()
    {
        var eventId = Guid.NewGuid();
        const string routingKey = "booking.booking.passenger_no_show_marked";
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
        RabbitMqEventPublisher Publisher() => new(connections, Options.Create(new RabbitMqOptions()),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());
        firstChannel.When(channel => channel.BasicPublish(
                "vietride.events", routingKey, false, firstProperties, Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(_ => throw new IOException("broker unavailable"));

        var first = () => Publisher().PublishRawAsync(routingKey, eventId, payload, CancellationToken.None);
        await first.Should().ThrowAsync<IOException>();
        await Publisher().PublishRawAsync(routingKey, eventId, payload, CancellationToken.None);

        firstProperties.MessageId.Should().Be(eventId.ToString("D"));
        secondChannel.Received(1).ExchangeDeclare("vietride.events", ExchangeType.Topic, true, false, null);
        secondChannel.Received(1).BasicPublish(
            "vietride.events", routingKey, false,
            Arg.Is<IBasicProperties>(properties => properties.MessageId == eventId.ToString("D")),
            Arg.Is<ReadOnlyMemory<byte>>(body => body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(payload))));
    }
}
