using FluentAssertions;
using NSubstitute;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

/// <summary>
/// Postgres-backed tests for the canonical <see cref="OutboxStore"/> and the
/// drain semantics the <c>OutboxBackgroundService</c> relies on. A small
/// harness replicates the worker's drain loop (fetch → publish → mark) against
/// a real database so we cover the store without standing up a BackgroundService.
/// </summary>
[Collection(OutboxStoreCollection.Name)]
public sealed class OutboxStoreTests : IAsyncLifetime
{
    private const int MaxRetryCount = 10;
    private const int BatchSize = 50;

    private readonly OutboxStoreFixture _fixture;

    public OutboxStoreTests(OutboxStoreFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DrainOnce_PendingRow_PublishesOnceAndMarksPublished()
    {
        var id = await SeedAsync(OutboxEventStatus.PENDING, retryCount: 0);
        var publisher = Substitute.For<IEventPublisher>();

        var drained = await DrainOnceAsync(publisher);

        drained.Should().Be(1);
        await publisher.Received(1).PublishRawAsync(
            "test.event", id, Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var ctx = _fixture.CreateContext();
        var row = await FindAsync(ctx, id);
        row.Status.Should().Be(OutboxEventStatus.PUBLISHED);
        row.PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DrainOnce_PublishFailure_IncrementsRetryAndIsRefetched()
    {
        var id = await SeedAsync(OutboxEventStatus.PENDING, retryCount: 0);
        var publisher = Substitute.For<IEventPublisher>();
        publisher
            .PublishRawAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("broker down"));

        var drained = await DrainOnceAsync(publisher);

        drained.Should().Be(0);

        await using (var ctx = _fixture.CreateContext())
        {
            var row = await FindAsync(ctx, id);
            row.Status.Should().Be(OutboxEventStatus.FAILED);
            row.RetryCount.Should().Be(1);
            row.LastError.Should().Be("broker down");
        }

        // FAILED row with RetryCount (1) <= MaxRetryCount is re-fetched next tick.
        await using (var ctx = _fixture.CreateContext())
        {
            var store = _fixture.CreateStore(ctx);
            var batch = await store.FetchPendingAsync(BatchSize, MaxRetryCount, CancellationToken.None);
            batch.Should().ContainSingle(e => e.Id == id);
        }
    }

    [Fact]
    public async Task FetchPending_FailedRowExceedingMaxRetry_IsParkedAndNotReturned()
    {
        var id = await SeedAsync(OutboxEventStatus.FAILED, retryCount: MaxRetryCount + 1);

        await using var ctx = _fixture.CreateContext();
        var store = _fixture.CreateStore(ctx);
        var batch = await store.FetchPendingAsync(BatchSize, MaxRetryCount, CancellationToken.None);

        batch.Should().NotContain(e => e.Id == id);
    }

    [Fact]
    public async Task FetchPending_ProjectsEnvelope_NextAttemptNullAndConvertsTimestamps()
    {
        // Non-UTC offsets — Npgsql timestamptz normalizes these to UTC on write,
        // and the store projects the stored DateTimeOffset back to a UTC DateTime.
        var created = new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.FromHours(7));
        var published = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.FromHours(7));
        var id = await SeedAsync(
            OutboxEventStatus.PENDING,
            retryCount: 0,
            createdAt: created.ToUniversalTime(),
            publishedAt: published.ToUniversalTime());

        await using var ctx = _fixture.CreateContext();
        var store = _fixture.CreateStore(ctx);
        var batch = await store.FetchPendingAsync(BatchSize, MaxRetryCount, CancellationToken.None);

        var envelope = batch.Should().ContainSingle(e => e.Id == id).Subject;
        envelope.NextAttemptAt.Should().BeNull();
        envelope.CreatedAt.Should().Be(created.UtcDateTime);
        envelope.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        envelope.PublishedAt.Should().Be(published.UtcDateTime);
    }

    // --- helpers -----------------------------------------------------------

    /// Replicates OutboxBackgroundService.DrainOnceAsync against the fixture store.
    private async Task<int> DrainOnceAsync(IEventPublisher publisher)
    {
        await using var ctx = _fixture.CreateContext();
        var store = _fixture.CreateStore(ctx);

        var batch = await store.FetchPendingAsync(BatchSize, MaxRetryCount, CancellationToken.None);
        var ok = 0;
        foreach (var evt in batch)
        {
            try
            {
                await publisher.PublishRawAsync(evt.EventType, evt.Id, evt.Payload, CancellationToken.None);
                await store.MarkPublishedAsync(evt.Id, _fixture.Clock.UtcNow.UtcDateTime, CancellationToken.None);
                ok++;
            }
            catch (Exception ex)
            {
                await store.MarkFailedAsync(
                    evt.Id, ex.Message, _fixture.Clock.UtcNow.UtcDateTime, CancellationToken.None);
            }
        }

        return ok;
    }

    private async Task<Guid> SeedAsync(
        OutboxEventStatus status,
        int retryCount,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? publishedAt = null)
    {
        await using var ctx = _fixture.CreateContext();
        var row = new OutboxEvent
        {
            EventType = "test.event",
            Payload = "{\"hello\":\"world\"}",
            Status = status,
            RetryCount = retryCount,
            CreatedAt = createdAt ?? _fixture.Clock.UtcNow,
            PublishedAt = publishedAt,
        };
        ctx.OutboxEvents.Add(row);
        await ctx.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<OutboxEvent> FindAsync(OutboxTestDbContext ctx, Guid id)
    {
        var row = await ctx.OutboxEvents.FindAsync(id);
        row.Should().NotBeNull();
        return row!;
    }
}
