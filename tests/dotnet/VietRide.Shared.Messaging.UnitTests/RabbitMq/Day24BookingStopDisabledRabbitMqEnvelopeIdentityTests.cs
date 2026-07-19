using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using VietRide.Shared.Messaging.RabbitMq;
using Xunit;

namespace VietRide.Shared.Messaging.UnitTests.RabbitMq;

public sealed class Day24BookingStopDisabledRabbitMqEnvelopeIdentityTests
{
    [Fact]
    public async Task PublishRaw_UsesTopicExchangeRoutingKeyAndOutboxIdentityAfterPublisherRecreation()
    {
        var eventId = Guid.NewGuid();
        var payload = "{\"eventType\":\"booking.stop_disabled.affected\"}";
        var pendingRow = new PendingOutboxRow(eventId, "booking.stop_disabled.affected", payload);
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
            connections, Options.Create(new RabbitMqOptions()),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());

        firstChannel
            .When(channel => channel.BasicPublish(
                "vietride.events", pendingRow.EventType, false,
                firstProperties, Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(_ => throw new IOException("simulated broker failure"));

        var firstAttempt = () => CreatePublisher().PublishRawAsync(
            pendingRow.EventType, pendingRow.Id, pendingRow.Payload, CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<IOException>();

        firstChannel.Received(1).ExchangeDeclare("vietride.events", ExchangeType.Topic, true, false, null);
        firstChannel.Received(1).BasicPublish(
            "vietride.events", "booking.stop_disabled.affected", false,
            firstProperties, Arg.Any<ReadOnlyMemory<byte>>());
        firstProperties.MessageId.Should().Be(eventId.ToString("D"));
        firstProperties.Type.Should().Be("booking.stop_disabled.affected");

        await CreatePublisher().PublishRawAsync(
            pendingRow.EventType, pendingRow.Id, pendingRow.Payload, CancellationToken.None);
        secondChannel.Received(1).ExchangeDeclare("vietride.events", ExchangeType.Topic, true, false, null);
        secondChannel.Received(1).BasicPublish(
            "vietride.events", pendingRow.EventType, false,
            Arg.Is<IBasicProperties>(value => value.MessageId == pendingRow.Id.ToString("D")),
            Arg.Is<ReadOnlyMemory<byte>>(body =>
                body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(pendingRow.Payload))));
        secondProperties.Should().NotBeSameAs(firstProperties);
        secondProperties.Type.Should().Be(pendingRow.EventType);
    }

    private sealed record PendingOutboxRow(Guid Id, string EventType, string Payload);
}
