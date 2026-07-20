using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Persistence.Outbox;
using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

[Collection(OutboxStoreCollection.Name)]
public sealed class Day24BookingStopDisabledOutboxIdentityTests : IAsyncLifetime
{
    private readonly OutboxStoreFixture _fixture;

    public Day24BookingStopDisabledOutboxIdentityTests(OutboxStoreFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BookingStopDisabledAffected_PersistsExactlyOnePendingRowWithProducerIdentity()
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            eventType = "booking.stop_disabled.affected",
            stopId = Guid.NewGuid(),
            recipientUserIds = new[] { Guid.NewGuid() },
            affectedBookingCount = 1,
        });

        await using (var write = _fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(_fixture.CreateStore(write));
            await outbox.EnqueueAsync(eventId, "booking.stop_disabled.affected", payload, CancellationToken.None);
            await write.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var rows = await read.OutboxEvents.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        var row = rows.Single();
        row.Id.Should().Be(eventId);
        row.EventType.Should().Be("booking.stop_disabled.affected");
        row.Status.Should().Be(OutboxEventStatus.PENDING);
        row.PublishedAt.Should().BeNull();
        JsonDocument.Parse(row.Payload).RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
    }
}
