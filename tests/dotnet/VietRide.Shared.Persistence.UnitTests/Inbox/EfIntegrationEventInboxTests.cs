using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Persistence.Inbox;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Shared.Persistence.UnitTests.Outbox;
using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Inbox;

[Collection(OutboxStoreCollection.Name)]
public sealed class EfIntegrationEventInboxTests : IAsyncLifetime
{
    private const string ConsumerName = "booking.payment-events";
    private const string PayloadHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly OutboxStoreFixture _fixture;

    public EfIntegrationEventInboxTests(OutboxStoreFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExecuteAsync_HandlerFailsAfterFlush_RollsBackDomainAndInboxTogether()
    {
        var messageId = Guid.NewGuid();
        var sideEffectId = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            var inbox = CreateInbox(context);

            var act = () => inbox.ExecuteAsync(
                ConsumerName,
                messageId,
                PayloadHash,
                async cancellationToken =>
                {
                    context.OutboxEvents.Add(CreateSideEffect(sideEffectId));
                    await context.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException("crash before inbox commit");
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("crash before inbox commit");
        }

        await using var verification = _fixture.CreateContext();
        (await verification.OutboxEvents.CountAsync(row => row.Id == sideEffectId)).Should().Be(0);
        (await verification.Set<IntegrationInboxRecord>().CountAsync(
            row => row.ConsumerName == ConsumerName && row.MessageId == messageId)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_RedeliveryAfterCommit_ReturnsDuplicateWithoutRepeatingSideEffect()
    {
        var messageId = Guid.NewGuid();
        var sideEffectId = Guid.NewGuid();
        var handlerCalls = 0;

        await using (var context = _fixture.CreateContext())
        {
            var inbox = CreateInbox(context);
            var first = await inbox.ExecuteAsync(
                ConsumerName,
                messageId,
                PayloadHash,
                _ =>
                {
                    handlerCalls++;
                    context.OutboxEvents.Add(CreateSideEffect(sideEffectId));
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            first.Should().Be(IntegrationEventInboxResult.Processed);
        }

        await using (var redeliveryContext = _fixture.CreateContext())
        {
            var inbox = CreateInbox(redeliveryContext);
            var redelivery = await inbox.ExecuteAsync(
                ConsumerName,
                messageId,
                PayloadHash,
                _ =>
                {
                    handlerCalls++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            redelivery.Should().Be(IntegrationEventInboxResult.Duplicate);
        }

        handlerCalls.Should().Be(1);
        await using var verification = _fixture.CreateContext();
        (await verification.OutboxEvents.CountAsync(row => row.Id == sideEffectId)).Should().Be(1);
        (await verification.Set<IntegrationInboxRecord>().CountAsync(
            row => row.ConsumerName == ConsumerName && row.MessageId == messageId)).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_SameMessageIdWithDifferentPayload_RejectsWithoutCallingHandler()
    {
        var messageId = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            var inbox = CreateInbox(context);
            await inbox.ExecuteAsync(
                ConsumerName,
                messageId,
                PayloadHash,
                _ => Task.CompletedTask,
                CancellationToken.None);
        }

        await using var mismatchContext = _fixture.CreateContext();
        var mismatchInbox = CreateInbox(mismatchContext);
        var handlerCalled = false;
        var act = () => mismatchInbox.ExecuteAsync(
            ConsumerName,
            messageId,
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            _ =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<IntegrationEventPayloadMismatchException>();
        handlerCalled.Should().BeFalse();
    }

    private EfIntegrationEventInbox<OutboxTestDbContext> CreateInbox(OutboxTestDbContext context)
        => new(context, new EfUnitOfWork(context), _fixture.Clock);

    private static OutboxEvent CreateSideEffect(Guid id)
        => new()
        {
            Id = id,
            EventType = "booking.booking.confirmed",
            Payload = "{}",
        };
}
