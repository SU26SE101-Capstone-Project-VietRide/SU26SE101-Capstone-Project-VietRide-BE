using System.Text.Json;
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

    [Fact]
    public async Task MoveToDlq_SixthFailure_PersistsExactlyOneTerminalRecord()
    {
        var id = await SeedAsync(OutboxEventStatus.FAILED, retryCount: 5);

        await using (var ctx = _fixture.CreateContext())
        {
            var store = _fixture.CreateStore(ctx);
            await store.MoveToDlqAsync(
                id,
                "broker unavailable",
                _fixture.Clock.UtcNow.UtcDateTime,
                CancellationToken.None);
        }

        await using (var ctx = _fixture.CreateContext())
        {
            var store = _fixture.CreateStore(ctx);
            await store.MoveToDlqAsync(
                id,
                "duplicate tick",
                _fixture.Clock.UtcNow.UtcDateTime,
                CancellationToken.None);
        }

        await using var assertion = _fixture.CreateContext();
        var dlq = assertion.OutboxDlq.Should().ContainSingle(row => row.EventId == id).Subject;
        dlq.RetryCount.Should().Be(6);
        dlq.EventType.Should().Be("test.event");
        using (var payload = JsonDocument.Parse(dlq.Payload))
        {
            payload.RootElement.GetProperty("hello").GetString().Should().Be("world");
        }
        dlq.LastError.Should().Be("broker unavailable");

        var source = await FindAsync(assertion, id);
        source.RetryCount.Should().Be(6);
        source.LastError.Should().Be("broker unavailable");
        var storeForFetch = _fixture.CreateStore(assertion);
        var pending = await storeForFetch.FetchPendingAsync(BatchSize, 5, CancellationToken.None);
        pending.Should().NotContain(row => row.Id == id);
    }

    [Fact]
    public async Task MoveToDlq_CustomRetryBoundary_DerivesTerminalCountFromSourceRow()
    {
        var id = await SeedAsync(OutboxEventStatus.FAILED, retryCount: 2);

        await using var ctx = _fixture.CreateContext();
        var store = _fixture.CreateStore(ctx);
        await store.MoveToDlqAsync(
            id,
            "configured boundary reached",
            _fixture.Clock.UtcNow.UtcDateTime,
            CancellationToken.None);

        var dlq = ctx.OutboxDlq.Should().ContainSingle(row => row.EventId == id).Subject;
        dlq.RetryCount.Should().Be(3);

        var source = await FindAsync(ctx, id);
        source.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task MoveToDlq_ConcurrentWorkers_CommitOneIdempotentTerminalRecord()
    {
        var id = await SeedAsync(OutboxEventStatus.FAILED, retryCount: 5);
        var first = MoveToDlqInNewContextAsync(id, "first terminal error");
        var second = MoveToDlqInNewContextAsync(id, "second terminal error");

        await Task.WhenAll(first, second);

        await using var assertion = _fixture.CreateContext();
        var terminal = assertion.OutboxDlq.Should()
            .ContainSingle(row => row.EventId == id)
            .Subject;
        terminal.RetryCount.Should().Be(6);
        terminal.LastError.Should().BeOneOf("first terminal error", "second terminal error");
        var source = await FindAsync(assertion, id);
        source.RetryCount.Should().Be(6);
        source.LastError.Should().Be(terminal.LastError);
    }

    [Fact]
    public async Task MoveToDlq_PublishedSource_DoesNotCreateTerminalRecord()
    {
        var id = await SeedAsync(
            OutboxEventStatus.PUBLISHED,
            retryCount: 5,
            publishedAt: _fixture.Clock.UtcNow);

        await MoveToDlqInNewContextAsync(id, "late failed worker");

        await using var assertion = _fixture.CreateContext();
        assertion.OutboxDlq.Should().NotContain(row => row.EventId == id);
        var source = await FindAsync(assertion, id);
        source.Status.Should().Be(OutboxEventStatus.PUBLISHED);
        source.RetryCount.Should().Be(5);
        source.LastError.Should().BeNull();
    }

    [Fact]
    public async Task PublishSuccessVsTerminalFailure_Race_CommitsOneConsistentOutcome()
    {
        var ids = new List<Guid>();
        for (var index = 0; index < 10; index++)
            ids.Add(await SeedAsync(OutboxEventStatus.FAILED, retryCount: 5));

        await Task.WhenAll(ids.SelectMany(id => new[]
        {
            MarkPublishedInNewContextAsync(id),
            MoveToDlqInNewContextAsync(id, "terminal worker"),
        }));

        await using var assertion = _fixture.CreateContext();
        foreach (var id in ids)
        {
            var source = await FindAsync(assertion, id);
            var terminal = assertion.OutboxDlq.SingleOrDefault(row => row.EventId == id);
            if (terminal is null)
            {
                source.Status.Should().Be(OutboxEventStatus.PUBLISHED);
                source.PublishedAt.Should().NotBeNull();
                source.LastError.Should().BeNull();
            }
            else
            {
                source.Status.Should().Be(OutboxEventStatus.FAILED);
                source.PublishedAt.Should().BeNull();
                source.RetryCount.Should().Be(terminal.RetryCount);
                source.LastError.Should().Be(terminal.LastError);
            }
        }
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

    private async Task MoveToDlqInNewContextAsync(Guid id, string error)
    {
        await using var context = _fixture.CreateContext();
        var store = _fixture.CreateStore(context);
        await store.MoveToDlqAsync(
            id,
            error,
            _fixture.Clock.UtcNow.UtcDateTime,
            CancellationToken.None);
    }

    private async Task MarkPublishedInNewContextAsync(Guid id)
    {
        await using var context = _fixture.CreateContext();
        var store = _fixture.CreateStore(context);
        await store.MarkPublishedAsync(
            id,
            _fixture.Clock.UtcNow.UtcDateTime,
            CancellationToken.None);
    }

    private static async Task<OutboxEvent> FindAsync(OutboxTestDbContext ctx, Guid id)
    {
        var row = await ctx.OutboxEvents.FindAsync(id);
        row.Should().NotBeNull();
        return row!;
    }
}
