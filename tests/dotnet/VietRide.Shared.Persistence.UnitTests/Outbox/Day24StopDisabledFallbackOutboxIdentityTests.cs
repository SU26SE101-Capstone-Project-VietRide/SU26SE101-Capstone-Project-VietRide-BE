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
public sealed class Day24StopDisabledFallbackOutboxIdentityTests : IAsyncLifetime
{
    private const string RoutingKey = "booking.booking.stop_disabled_auto_fallback_applied";
    private readonly OutboxStoreFixture _fixture;

    public Day24StopDisabledFallbackOutboxIdentityTests(OutboxStoreFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PendingIdentity_SurvivesFailureAndIsDeliveredAfterWorkerRestart()
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId });
        await using (var write = _fixture.CreateContext())
        {
            var outbox = new IntegrationEventOutbox(_fixture.CreateStore(write));
            await outbox.EnqueueAsync(eventId, RoutingKey, payload, CancellationToken.None);
            await write.SaveChangesAsync();
        }

        string persistedPayload;
        await using (var pending = _fixture.CreateContext())
        {
            var row = await pending.OutboxEvents.AsNoTracking().SingleAsync();
            row.Id.Should().Be(eventId);
            row.EventType.Should().Be(RoutingKey);
            row.Status.Should().Be(OutboxEventStatus.PENDING);
            row.PublishedAt.Should().BeNull();
            persistedPayload = row.Payload;
        }

        var failedPublisher = Substitute.For<IEventPublisher>();
        failedPublisher.PublishRawAsync(RoutingKey, eventId, persistedPayload, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("broker unavailable"));
        await using (var firstContext = _fixture.CreateContext())
        {
            (await CreateWorker(_fixture.CreateStore(firstContext), failedPublisher)
                .DrainOnceAsync(CancellationToken.None)).Should().Be(0);
        }

        var restartedPublisher = Substitute.For<IEventPublisher>();
        await using (var restartedContext = _fixture.CreateContext())
        {
            (await CreateWorker(_fixture.CreateStore(restartedContext), restartedPublisher)
                .DrainOnceAsync(CancellationToken.None)).Should().Be(1);
        }

        await failedPublisher.Received(1).PublishRawAsync(
            RoutingKey, eventId, persistedPayload, Arg.Any<CancellationToken>());
        await restartedPublisher.Received(1).PublishRawAsync(
            RoutingKey, eventId, persistedPayload, Arg.Any<CancellationToken>());
        await using var verify = _fixture.CreateContext();
        var delivered = await verify.OutboxEvents.SingleAsync();
        delivered.Id.Should().Be(eventId);
        delivered.Status.Should().Be(OutboxEventStatus.PUBLISHED);
        delivered.PublishedAt.Should().NotBeNull();
    }

    private static OutboxBackgroundService CreateWorker(IOutboxStore store, IEventPublisher publisher)
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IOutboxStore)).Returns(store);
        provider.GetService(typeof(IEventPublisher)).Returns(publisher);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);
        return new OutboxBackgroundService(
            scopes,
            Options.Create(new OutboxOptions()),
            Substitute.For<ILogger<OutboxBackgroundService>>());
    }
}
