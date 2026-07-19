using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Messaging.Outbox;
using VietRide.Shared.Persistence.Outbox;
using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

[Collection(OutboxStoreCollection.Name)]
public sealed class Day24PassengerNoShowOutboxIdentityTests : IAsyncLifetime
{
    private const string RoutingKey = "booking.booking.passenger_no_show_marked";
    private readonly OutboxStoreFixture _fixture;

    public Day24PassengerNoShowOutboxIdentityTests(OutboxStoreFixture fixture) => _fixture = fixture;
    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EventIsPersistedOnceWithPendingProducerIdentity()
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId, eventType = RoutingKey });
        await using (var write = _fixture.CreateContext())
        {
            await new IntegrationEventOutbox(_fixture.CreateStore(write))
                .EnqueueAsync(eventId, RoutingKey, payload, CancellationToken.None);
            await write.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var row = await read.OutboxEvents.AsNoTracking().SingleAsync();
        row.Id.Should().Be(eventId);
        row.EventType.Should().Be(RoutingKey);
        row.Status.Should().Be(OutboxEventStatus.PENDING);
        row.PublishedAt.Should().BeNull();
        JsonDocument.Parse(row.Payload).RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
    }
}
