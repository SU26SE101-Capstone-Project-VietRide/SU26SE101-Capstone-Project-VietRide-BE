using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Persistence.Outbox;
using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

[Collection(OutboxStoreCollection.Name)]
public sealed class Day23ExplicitOutboxIdentityTests : IAsyncLifetime
{
    private readonly OutboxStoreFixture _fixture;

    public Day23ExplicitOutboxIdentityTests(OutboxStoreFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExplicitIdentity_PersistsSuppliedIdAlongsideMatchingPayloadIdentity()
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId });

        await using (var writeContext = _fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(_fixture.CreateStore(writeContext));

            await outbox.EnqueueAsync(
                eventId,
                "trip.trip.schedule_changed",
                payload,
                CancellationToken.None);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var row = await readContext.OutboxEvents.SingleAsync();

        row.Id.Should().Be(eventId);
        row.EventType.Should().Be("trip.trip.schedule_changed");
        row.Status.Should().Be(OutboxEventStatus.PENDING);
        using var persistedPayload = JsonDocument.Parse(row.Payload);
        persistedPayload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
    }

    [Fact]
    public async Task ExplicitIdentity_RejectsEmptyIdWithoutEnlistingAnOutboxRow()
    {
        await using var context = _fixture.CreateContext();
        var outbox = new IntegrationEventOutbox(_fixture.CreateStore(context));

        var act = () => outbox.EnqueueAsync(
            Guid.Empty,
            "trip.trip.schedule_changed",
            "{}",
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("eventId");
        context.ChangeTracker.Entries<OutboxEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyOverload_RemainsCompatibleAndAllocatesANewIdentity()
    {
        await using (var writeContext = _fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(_fixture.CreateStore(writeContext));

            await outbox.EnqueueAsync(
                "identity.user.created",
                "{\"userId\":\"legacy-compatible\"}",
                CancellationToken.None);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var row = await readContext.OutboxEvents.SingleAsync();

        row.Id.Should().NotBe(Guid.Empty);
        row.EventType.Should().Be("identity.user.created");
        row.Status.Should().Be(OutboxEventStatus.PENDING);
        using var persistedPayload = JsonDocument.Parse(row.Payload);
        persistedPayload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
    }

    [Fact]
    public async Task LegacyOverload_UsesPayloadIdentityAsCanonicalOutboxIdentity()
    {
        var eventId = Guid.NewGuid();

        await using (var writeContext = _fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(_fixture.CreateStore(writeContext));

            await outbox.EnqueueAsync(
                "payment.payment.succeeded",
                JsonSerializer.Serialize(new { eventId, paymentId = Guid.NewGuid() }),
                CancellationToken.None);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var row = await readContext.OutboxEvents.SingleAsync();

        row.Id.Should().Be(eventId);
        using var persistedPayload = JsonDocument.Parse(row.Payload);
        persistedPayload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
    }

    [Fact]
    public async Task ExplicitIdentity_RejectsPayloadWithDifferentIdentity()
    {
        await using var context = _fixture.CreateContext();
        var outbox = new IntegrationEventOutbox(_fixture.CreateStore(context));

        var act = () => outbox.EnqueueAsync(
            Guid.NewGuid(),
            "payment.payment.succeeded",
            JsonSerializer.Serialize(new { eventId = Guid.NewGuid() }),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("payloadJson")
            .WithMessage("*eventId does not match*");
        context.ChangeTracker.Entries<OutboxEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultExplicitIdentity_KeepsLegacyOnlyImplementationsSourceCompatibleAndFailsFast()
    {
        IIntegrationEventOutbox legacyOnlyOutbox = new LegacyOnlyOutbox();

        var emptyIdAct = () => legacyOnlyOutbox.EnqueueAsync(
            Guid.Empty,
            "trip.trip.schedule_changed",
            "{}",
            CancellationToken.None);
        var unsupportedAct = () => legacyOnlyOutbox.EnqueueAsync(
            Guid.NewGuid(),
            "trip.trip.schedule_changed",
            "{}",
            CancellationToken.None);

        await emptyIdAct.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("eventId");
        await unsupportedAct.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*does not support producer-supplied event identities*");
    }

    private sealed class LegacyOnlyOutbox : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
