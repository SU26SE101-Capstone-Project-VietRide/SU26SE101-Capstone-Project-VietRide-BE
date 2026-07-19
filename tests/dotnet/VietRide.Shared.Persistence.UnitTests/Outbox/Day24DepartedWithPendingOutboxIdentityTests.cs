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
public sealed class Day24DepartedWithPendingOutboxIdentityTests : IAsyncLifetime
{
    private const string RoutingKey = "trip.stop.departed_with_pending";
    private readonly OutboxStoreFixture fixture;

    public Day24DepartedWithPendingOutboxIdentityTests(OutboxStoreFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Restart_RedeliversSameUnprocessedDepartedPendingIdentity()
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            eventType = RoutingKey,
            tripId = Guid.NewGuid(),
            stopId = Guid.NewGuid(),
            pendingPassengerCount = 2,
        });
        await using (var context = fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(fixture.CreateStore(context));
            await outbox.EnqueueAsync(eventId, RoutingKey, payload, CancellationToken.None);
            await context.SaveChangesAsync();
        }

        string persistedPayload;
        await using (var persistedContext = fixture.CreateContext())
        {
            var row = await persistedContext.OutboxEvents.AsNoTracking().SingleAsync();
            row.Id.Should().Be(eventId);
            row.EventType.Should().Be(RoutingKey);
            row.Status.Should().Be(OutboxEventStatus.PENDING);
            row.PublishedAt.Should().BeNull();
            persistedPayload = row.Payload;
            using var document = JsonDocument.Parse(row.Payload);
            document.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
        }

        var unavailable = Substitute.For<IEventPublisher>();
        unavailable.PublishRawAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("broker unavailable"));
        await using (var failedContext = fixture.CreateContext())
        {
            var worker = CreateWorker(fixture.CreateStore(failedContext), unavailable);
            (await worker.DrainOnceAsync(CancellationToken.None)).Should().Be(0);
        }

        var restarted = Substitute.For<IEventPublisher>();
        await using (var restartedContext = fixture.CreateContext())
        {
            var worker = CreateWorker(fixture.CreateStore(restartedContext), restarted);
            (await worker.DrainOnceAsync(CancellationToken.None)).Should().Be(1);
        }

        await restarted.Received(1).PublishRawAsync(
            RoutingKey,
            eventId,
            persistedPayload,
            Arg.Any<CancellationToken>());
        await using var verification = fixture.CreateContext();
        var persisted = await verification.OutboxEvents.SingleAsync();
        persisted.Id.Should().Be(eventId);
        persisted.Status.Should().Be(OutboxEventStatus.PUBLISHED);
        persisted.RetryCount.Should().Be(1);
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
