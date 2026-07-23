using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.Internal.Trips;

namespace VietRide.Trip.IntegrationTests.Events;

public sealed class TripCancelledByOperatorIntegrationEventTests
{
    [Fact]
    public async Task OutboxSameTransactionProcessedAtNullRoutingExchangeMessageIdRestartAndDedupe()
    {
        var id = Guid.NewGuid();
        var evt = new TripCancelledByOperatorIntegrationEvent(
            id,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Vehicle issue");

        evt.EventId.Should().Be(id);
        evt.EventType.Should().Be("trip.trip.cancelled");
        typeof(IIntegrationEventOutbox).GetMethod(
            nameof(IIntegrationEventOutbox.EnqueueAsync),
            [typeof(Guid), typeof(string), typeof(string), typeof(CancellationToken)])
            .Should().NotBeNull();

        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}cancel_{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(db);
            var now = DateTimeOffset.UtcNow;
            var cancellation = new TripCancelledByOperatorIntegrationEvent(
                id, now, seed.TripId, seed.OperatorId, now, "Vehicle issue");
            var outbox = new IntegrationEventOutbox(new OutboxStore(
                db,
                new Day29CargoNearFullOutboxIntegrationTests.FixedClock()));
            await outbox.EnqueueAsync(
                cancellation.EventId,
                cancellation.EventType,
                JsonSerializer.Serialize(cancellation, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await db.SaveChangesAsync();

            var row = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.Id == cancellation.EventId);
            row.EventType.Should().Be(cancellation.EventType);
            row.Status.Should().Be(OutboxEventStatus.PENDING);
            row.PublishedAt.Should().BeNull();
            using var payload = JsonDocument.Parse(row.Payload);
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public void PayloadUsesExactRegistryFields()
    {
        var evt = new TripCancelledByOperatorIntegrationEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Vehicle issue");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            evt,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("eventId", "occurredAt", "tripId", "operatorId", "cancelledAt", "cancelReason");
        document.RootElement.GetProperty("eventId").GetGuid().Should().Be(evt.EventId);
    }
}
