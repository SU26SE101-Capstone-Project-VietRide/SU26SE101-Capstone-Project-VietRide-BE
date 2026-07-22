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
public sealed class Day29ParcelAutoRejectedOutboxRestartTests : IAsyncLifetime
{
    private const string RoutingKey = "parcel.parcel.auto_rejected";
    private readonly OutboxStoreFixture fixture;

    public Day29ParcelAutoRejectedOutboxRestartTests(OutboxStoreFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PendingAutoRejectedRow_SurvivesFailureAndIsDeliveredAfterWorkerRestart()
    {
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

        await using (var enqueueContext = fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(fixture.CreateStore(enqueueContext));
            await outbox.EnqueueAsync(eventId, RoutingKey, payload, CancellationToken.None);
            await enqueueContext.SaveChangesAsync();
        }

        string persistedPayload;
        await using (var pendingContext = fixture.CreateContext())
        {
            var pending = await pendingContext.OutboxEvents.AsNoTracking().SingleAsync();
            pending.Id.Should().Be(eventId);
            pending.EventType.Should().Be(RoutingKey);
            pending.Status.Should().Be(OutboxEventStatus.PENDING);
            pending.PublishedAt.Should().BeNull();
            persistedPayload = pending.Payload;
            using var json = JsonDocument.Parse(persistedPayload);
            json.RootElement.GetProperty("eventId").GetGuid().Should().Be(eventId);
        }

        var unavailablePublisher = Substitute.For<IEventPublisher>();
        unavailablePublisher.PublishRawAsync(RoutingKey, eventId, persistedPayload, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("broker unavailable"));
        await using (var failedContext = fixture.CreateContext())
        {
            var firstWorker = CreateWorker(fixture.CreateStore(failedContext), unavailablePublisher);
            (await firstWorker.DrainOnceAsync(CancellationToken.None)).Should().Be(0);
        }

        var restartedPublisher = Substitute.For<IEventPublisher>();
        await using (var restartedContext = fixture.CreateContext())
        {
            var restartedWorker = CreateWorker(fixture.CreateStore(restartedContext), restartedPublisher);
            (await restartedWorker.DrainOnceAsync(CancellationToken.None)).Should().Be(1);
        }

        await unavailablePublisher.Received(1).PublishRawAsync(
            RoutingKey,
            eventId,
            persistedPayload,
            Arg.Any<CancellationToken>());
        await restartedPublisher.Received(1).PublishRawAsync(
            RoutingKey,
            eventId,
            persistedPayload,
            Arg.Any<CancellationToken>());

        await using var verificationContext = fixture.CreateContext();
        var delivered = await verificationContext.OutboxEvents.AsNoTracking().SingleAsync();
        delivered.Id.Should().Be(eventId);
        delivered.EventType.Should().Be(RoutingKey);
        delivered.Payload.Should().Be(persistedPayload);
        delivered.Status.Should().Be(OutboxEventStatus.PUBLISHED);
        delivered.PublishedAt.Should().NotBeNull();
        delivered.RetryCount.Should().Be(1);
    }

    private static OutboxBackgroundService CreateWorker(IOutboxStore store, IEventPublisher publisher)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxStore)).Returns(store);
        serviceProvider.GetService(typeof(IEventPublisher)).Returns(publisher);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        return new OutboxBackgroundService(
            scopeFactory,
            Options.Create(new OutboxOptions()),
            Substitute.For<ILogger<OutboxBackgroundService>>());
    }
}
