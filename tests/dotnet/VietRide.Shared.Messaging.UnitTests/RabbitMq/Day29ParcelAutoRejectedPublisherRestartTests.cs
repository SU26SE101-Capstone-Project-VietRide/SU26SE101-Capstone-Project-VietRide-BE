using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using VietRide.Shared.Kernel.Serialization;
using VietRide.Shared.Messaging.RabbitMq;
using Xunit;

namespace VietRide.Shared.Messaging.UnitTests.RabbitMq;

public sealed class Day29ParcelAutoRejectedPublisherRestartTests
{
    [Fact]
    public async Task Restart_PublishesAutoRejectedWithStableMessageId()
    {
        const string routingKey = "parcel.parcel.auto_rejected";
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            occurredAt = DateTimeOffset.UtcNow,
            parcelId = Guid.NewGuid(),
            parcelCode = "VRP-DAY29",
            operatorId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            tripId = Guid.NewGuid(),
            refundAmount = 100_000L,
        });
        var expectedPayload = UtcJson.NormalizeInstants(payload);
        var firstProperties = Substitute.For<IBasicProperties>();
        var restartedProperties = Substitute.For<IBasicProperties>();
        var firstChannel = Substitute.For<IModel>();
        var restartedChannel = Substitute.For<IModel>();
        firstChannel.IsOpen.Returns(true);
        restartedChannel.IsOpen.Returns(true);
        firstChannel.CreateBasicProperties().Returns(firstProperties);
        restartedChannel.CreateBasicProperties().Returns(restartedProperties);
        var firstConnection = Substitute.For<IConnection>();
        var restartedConnection = Substitute.For<IConnection>();
        firstConnection.CreateModel().Returns(firstChannel);
        restartedConnection.CreateModel().Returns(restartedChannel);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(firstConnection, restartedConnection);

        RabbitMqEventPublisher CreatePublisher() => new(
            connections,
            Options.Create(new RabbitMqOptions()),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());

        firstChannel.When(channel => channel.BasicPublish(
                "vietride.events",
                routingKey,
                false,
                firstProperties,
                Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(_ => throw new IOException("simulated broker failure"));

        var firstAttempt = () => CreatePublisher().PublishRawAsync(
            routingKey,
            eventId,
            payload,
            CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<IOException>();
        firstProperties.MessageId.Should().Be(eventId.ToString("D"));
        firstProperties.Type.Should().Be(routingKey);

        await CreatePublisher().PublishRawAsync(routingKey, eventId, payload, CancellationToken.None);

        restartedChannel.Received(1).ExchangeDeclare("vietride.events", ExchangeType.Topic, true, false, null);
        restartedChannel.Received(1).BasicPublish(
            "vietride.events",
            routingKey,
            false,
            Arg.Is<IBasicProperties>(properties =>
                properties.MessageId == eventId.ToString("D")
                && properties.Type == routingKey
                && properties.DeliveryMode == 2),
            Arg.Is<ReadOnlyMemory<byte>>(body =>
                body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(expectedPayload))));
    }
}
