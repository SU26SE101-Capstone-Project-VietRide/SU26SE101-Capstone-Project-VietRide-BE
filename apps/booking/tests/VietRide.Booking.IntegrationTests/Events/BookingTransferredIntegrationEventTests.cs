using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Messaging.RabbitMq;

namespace VietRide.Booking.IntegrationTests.Events;

public sealed class BookingTransferredIntegrationEventTests
{
    [Fact]
    public async Task EmitsOneExactFactPerBookingForOwnerEvenWhenNotificationIsSuppressed()
    {
        await TripVehicleSubstitutedConsumerTests.WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var first = TripVehicleSubstitutedConsumerTests.CreateConfirmedBooking(
                oldTripId,
                operatorId,
                "A01");
            var second = TripVehicleSubstitutedConsumerTests.CreateConfirmedBooking(
                oldTripId,
                operatorId,
                "A02");
            await TripVehicleSubstitutedConsumerTests.SeedAsync(dataSource, first, second);
            var evt = TripVehicleSubstitutedConsumerTests.CreateEvent(
                oldTripId,
                operatorId,
                first,
                [(first.Passengers[0], "B01", "PENDING")],
                [
                    new TripVehicleSubstitutedMapping
                    {
                        BookingId = second.Id,
                        PassengerId = second.Passengers[0].Id,
                        OriginalSeatNumber = "A02",
                        NewSeatNumber = null,
                        OriginalBoardingStatus = "PENDING",
                    },
                ]);
            await TripVehicleSubstitutedConsumerTests.ConsumeAsync(dataSource, evt);

            await using var verify = Day22EventDatabase.CreateDbContext(
                dataSource,
                DateTimeOffset.Parse("2026-07-26T03:00:00Z"));
            var rows = await verify.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == BookingTransferredIntegrationEvent.EventTypeValue)
                .OrderBy(row => row.Id)
                .ToArrayAsync();
            rows.Should().HaveCount(2);
            foreach (var row in rows)
            {
                using var payload = JsonDocument.Parse(row.Payload);
                var root = payload.RootElement;
                root.GetProperty("eventId").GetGuid().Should().Be(row.Id);
                root.GetProperty("sourceSubstitutionEventId").GetGuid().Should().Be(evt.EventId);
                root.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
                root.GetProperty("oldTripId").GetGuid().Should().Be(oldTripId);
                root.GetProperty("newTripId").GetGuid().Should().Be(evt.NewTripId);
                root.GetProperty("notifyPassengers").GetBoolean().Should().BeFalse();
                var bookingId = root.GetProperty("bookingId").GetGuid();
                var expectedOwner = bookingId == first.Id
                    ? first.PassengerUserId
                    : second.PassengerUserId;
                root.GetProperty("recipientUserId").GetGuid().Should().Be(expectedOwner);
                root.GetProperty("transfers").GetArrayLength().Should().Be(1);
            }
        });
    }

    [Fact]
    public async Task OutboxIsAtomicAndPublisherRestartPreservesRoutingKeyMessageIdAndPayload()
    {
        await TripVehicleSubstitutedConsumerTests.WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var booking = TripVehicleSubstitutedConsumerTests.CreateConfirmedBooking(
                oldTripId,
                operatorId,
                "A01");
            await TripVehicleSubstitutedConsumerTests.SeedAsync(dataSource, booking);
            var evt = TripVehicleSubstitutedConsumerTests.CreateEvent(
                oldTripId,
                operatorId,
                booking,
                (booking.Passengers[0], "B01", "PENDING"));
            await TripVehicleSubstitutedConsumerTests.ConsumeAsync(dataSource, evt);

            await using var verify = Day22EventDatabase.CreateDbContext(
                dataSource,
                DateTimeOffset.Parse("2026-07-26T03:00:00Z"));
            var row = await verify.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.EventType == BookingTransferredIntegrationEvent.EventTypeValue);
            row.PublishedAt.Should().BeNull();

            var firstConnections = Substitute.For<IRabbitMqConnectionFactory>();
            firstConnections.GetOrCreate().Returns(_ => throw new InvalidOperationException("broker unavailable"));
            var firstPublisher = CreatePublisher(firstConnections);
            var firstAttempt = () => firstPublisher.PublishRawAsync(
                row.EventType,
                row.Id,
                row.Payload,
                CancellationToken.None);
            await firstAttempt.Should().ThrowAsync<InvalidOperationException>();

            var connection = Substitute.For<IConnection>();
            var channel = Substitute.For<IModel>();
            var properties = Substitute.For<IBasicProperties>();
            connection.CreateModel().Returns(channel);
            channel.IsOpen.Returns(true);
            channel.CreateBasicProperties().Returns(properties);
            var restartedConnections = Substitute.For<IRabbitMqConnectionFactory>();
            restartedConnections.GetOrCreate().Returns(connection);
            var restartedPublisher = CreatePublisher(restartedConnections);
            await restartedPublisher.PublishRawAsync(
                row.EventType,
                row.Id,
                row.Payload,
                CancellationToken.None);

            properties.MessageId.Should().Be(row.Id.ToString());
            properties.Type.Should().Be(BookingTransferredIntegrationEvent.EventTypeValue);
            channel.Received(1).BasicPublish(
                "vietride.events",
                BookingTransferredIntegrationEvent.EventTypeValue,
                false,
                properties,
                Arg.Is<ReadOnlyMemory<byte>>(body =>
                    Encoding.UTF8.GetString(body.ToArray()) == row.Payload));
        });
    }

    [Fact]
    public void SerializedPayloadMatchesSharedContractFieldForField()
    {
        var evt = new BookingTransferredIntegrationEvent(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            DateTimeOffset.Parse("2026-07-26T03:00:00Z"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            Guid.Parse("55555555-5555-4555-8555-555555555555"),
            Guid.Parse("66666666-6666-4666-8666-666666666666"),
            Guid.Parse("77777777-7777-4777-8777-777777777777"),
            Guid.Parse("88888888-8888-4888-8888-888888888888"),
            "51B-999.99",
            DateTimeOffset.Parse("2026-07-26T03:30:00Z"),
            false,
            [
                new BookingTransferredIntegrationEvent.Transfer(
                    Guid.Parse("99999999-9999-4999-8999-999999999999"),
                    null,
                    "B01",
                    "PENDING_CONFIRM"),
            ]);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(
            evt,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "eventId",
            "occurredAt",
            "sourceSubstitutionEventId",
            "bookingId",
            "recipientUserId",
            "operatorId",
            "oldTripId",
            "newTripId",
            "newVehicleId",
            "newVehiclePlateNumber",
            "newTripDepartureDateTime",
            "notifyPassengers",
            "transfers");
        var transfer = payload.RootElement.GetProperty("transfers")[0];
        transfer.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "passengerId",
            "originalSeatNumber",
            "newSeatNumber",
            "confirmationStatus");
        transfer.GetProperty("originalSeatNumber").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static RabbitMqEventPublisher CreatePublisher(IRabbitMqConnectionFactory connections)
        => new(
            connections,
            Options.Create(new RabbitMqOptions
            {
                ExchangeName = "vietride.events",
            }),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());
}
