using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.Outbox;
using VietRide.Shared.Persistence.Outbox;
using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

[Collection(OutboxStoreCollection.Name)]
public sealed class Day23OutboxRestartIdentityTests : IAsyncLifetime
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

    private readonly OutboxStoreFixture _fixture;

    public Day23OutboxRestartIdentityTests(OutboxStoreFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(Day23RoutingKeys))]
    public async Task Restart_RedeliversSamePersistedIdentity(string routingKey)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId, routingKey });

        await using (var enqueueContext = _fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(_fixture.CreateStore(enqueueContext));
            await outbox.EnqueueAsync(eventId, routingKey, payload, CancellationToken.None);
            await enqueueContext.SaveChangesAsync();
        }

        string persistedPayload;
        await using (var persistedContext = _fixture.CreateContext())
        {
            var enqueuedRow = await persistedContext.OutboxEvents.AsNoTracking().SingleAsync();
            enqueuedRow.Id.Should().Be(eventId);
            persistedPayload = enqueuedRow.Payload;
            using var payloadDocument = JsonDocument.Parse(persistedPayload);
            payloadDocument.RootElement.GetProperty("eventId").GetGuid().Should().Be(eventId);
        }

        var unavailablePublisher = Substitute.For<IEventPublisher>();
        unavailablePublisher
            .PublishRawAsync(routingKey, eventId, persistedPayload, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("broker unavailable"));

        await using (var firstProcessContext = _fixture.CreateContext())
        {
            var firstWorker = CreateWorker(
                _fixture.CreateStore(firstProcessContext),
                unavailablePublisher);

            var firstDrainCount = await firstWorker.DrainOnceAsync(CancellationToken.None);

            firstDrainCount.Should().Be(0);
        }

        var restartedPublisher = Substitute.For<IEventPublisher>();
        await using (var restartedProcessContext = _fixture.CreateContext())
        {
            var restartedWorker = CreateWorker(
                _fixture.CreateStore(restartedProcessContext),
                restartedPublisher);

            var restartedDrainCount = await restartedWorker.DrainOnceAsync(CancellationToken.None);

            restartedDrainCount.Should().Be(1);
        }

        await unavailablePublisher.Received(1).PublishRawAsync(
            routingKey,
            eventId,
            persistedPayload,
            Arg.Any<CancellationToken>());
        await restartedPublisher.Received(1).PublishRawAsync(
            routingKey,
            eventId,
            persistedPayload,
            Arg.Any<CancellationToken>());

        await using var verificationContext = _fixture.CreateContext();
        var persistedRow = await verificationContext.OutboxEvents.SingleAsync();
        persistedRow.Id.Should().Be(eventId);
        persistedRow.EventType.Should().Be(routingKey);
        persistedRow.Payload.Should().Be(persistedPayload);
        persistedRow.Status.Should().Be(OutboxEventStatus.PUBLISHED);
        persistedRow.RetryCount.Should().Be(1);
    }

    private static OutboxBackgroundService CreateWorker(
        IOutboxStore store,
        IEventPublisher publisher)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxStore)).Returns(store);
        serviceProvider.GetService(typeof(IEventPublisher)).Returns(publisher);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        return new OutboxBackgroundService(
            scopes,
            Options.Create(new OutboxOptions()),
            Substitute.For<ILogger<OutboxBackgroundService>>());
    }
}
