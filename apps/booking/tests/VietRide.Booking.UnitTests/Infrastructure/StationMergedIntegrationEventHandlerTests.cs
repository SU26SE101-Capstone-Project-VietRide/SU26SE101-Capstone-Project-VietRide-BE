using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Infrastructure.Messaging;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class StationMergedIntegrationEventHandlerTests
{
    [Fact]
    public async Task Handle_MapsContractFieldsAndIgnoresUnusedSnapshots()
    {
        var eventId = Guid.NewGuid();
        var primaryStationId = Guid.NewGuid();
        var duplicateStationId = Guid.NewGuid();
        var occurredAt = DateTime.Parse(
            "2026-07-16T08:00:00Z",
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var payload = $$"""
            {
              "eventId": "{{eventId}}",
              "occurredAt": "{{occurredAt:O}}",
              "eventType": "trip.station.merged",
              "actorUserId": "{{Guid.NewGuid()}}",
              "primaryStationId": "{{primaryStationId}}",
              "duplicateStationId": "{{duplicateStationId}}",
              "primaryBefore": { "id": "{{primaryStationId}}", "name": "Primary" },
              "duplicateBefore": { "id": "{{duplicateStationId}}", "name": "Duplicate" },
              "primaryAfter": { "id": "{{primaryStationId}}", "name": "Primary" },
              "relinkedCounts": { "routeOrigins": 1 }
            }
            """;
        var integrationEvent = JsonSerializer.Deserialize<StationMergedIntegrationEvent>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var repository = Substitute.For<IBookingStationRedirectRepository>();
        var handler = new StationMergedIntegrationEventHandler(repository);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await repository.Received(1).ApplyMergeAsync(
            eventId,
            new DateTimeOffset(occurredAt),
            primaryStationId,
            duplicateStationId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingOccurredAtRejectsWithoutMarkerWrite()
    {
        var repository = Substitute.For<IBookingStationRedirectRepository>();
        var handler = new StationMergedIntegrationEventHandler(repository);
        var integrationEvent = new StationMergedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            PrimaryStationId = Guid.NewGuid(),
            DuplicateStationId = Guid.NewGuid(),
        };

        var action = () => handler.HandleAsync(integrationEvent, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        await repository.DidNotReceiveWithAnyArgs().ApplyMergeAsync(default, default, default, default, default);
    }
}
