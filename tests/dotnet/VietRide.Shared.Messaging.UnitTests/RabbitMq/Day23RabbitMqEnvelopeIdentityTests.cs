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

public sealed class Day23RabbitMqEnvelopeIdentityTests
{
    public static TheoryData<string> Day23RoutingKeys => new()
    {
        "trip.trip.schedule_changed",
        "booking.booking.schedule_change_informational",
        "booking.booking.schedule_change_required",
        "booking.booking.pending_action_realerted",
        "booking.booking.pending_action_auto_resolved",
        "booking.booking.cancelled",
    };

    [Theory]
    [MemberData(nameof(Day23RoutingKeys))]
    public async Task PublishRaw_UsesRowIdentityAndPersistentCanonicalEnvelope(string routingKey)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId, routingKey });
        var properties = Substitute.For<IBasicProperties>();
        var channel = Substitute.For<IModel>();
        channel.IsOpen.Returns(true);
        channel.CreateBasicProperties().Returns(properties);

        string? capturedExchange = null;
        string? capturedRoutingKey = null;
        bool? capturedMandatory = null;
        IBasicProperties? capturedProperties = null;
        ReadOnlyMemory<byte> capturedBody = default;
        channel
            .When(model => model.BasicPublish(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<IBasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(call =>
            {
                capturedExchange = call.ArgAt<string>(0);
                capturedRoutingKey = call.ArgAt<string>(1);
                capturedMandatory = call.ArgAt<bool>(2);
                capturedProperties = call.ArgAt<IBasicProperties>(3);
                capturedBody = call.ArgAt<ReadOnlyMemory<byte>>(4);
            });

        var connection = Substitute.For<IConnection>();
        connection.CreateModel().Returns(channel);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var publisher = new RabbitMqEventPublisher(
            connections,
            Options.Create(new RabbitMqOptions()),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());

        await publisher.PublishRawAsync(
            routingKey,
            eventId,
            payload,
            CancellationToken.None);

        channel.Received(1).ExchangeDeclare(
            "vietride.events",
            ExchangeType.Topic,
            true,
            false,
            null);
        capturedExchange.Should().Be("vietride.events");
        capturedRoutingKey.Should().Be(routingKey);
        capturedMandatory.Should().BeFalse();
        capturedProperties.Should().BeSameAs(properties);
        properties.ContentType.Should().Be("application/json");
        properties.ContentEncoding.Should().Be("utf-8");
        properties.DeliveryMode.Should().Be(2);
        properties.MessageId.Should().Be(eventId.ToString("D"));
        properties.Type.Should().Be(routingKey);
        Encoding.UTF8.GetString(capturedBody.Span).Should().Be(payload);
    }
}
