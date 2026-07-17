using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.ResolvePendingAction;

public sealed class Day23ResolveScheduleActionTransactionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T08:00:00Z");

    [Fact]
    public async Task RealPostgresCommitAndForcedFailureProveCancellationHistoryAndOutboxAtomicity()
    {
        var databaseName = $"vr_d23_resolve_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var committed = CreateAggregate(100_001);
            var rolledBack = CreateAggregate(200_001);
            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, Now))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.AddRange(committed.Booking, rolledBack.Booking);
                seed.BookingPendingActions.AddRange(committed.Action, rolledBack.Action);
                await seed.SaveChangesAsync();
            }

            await using (var commitDb = Day22EventDatabase.CreateDbContext(dataSource, Now))
            {
                var result = await CreateHandler(commitDb).Handle(
                    Command(committed.Booking, committed.Action, "REJECTED"),
                    CancellationToken.None);
                result.ResolvedAction.Should().Be("REJECTED");
            }

            await using (var verifyCommit = Day22EventDatabase.CreateDbContext(dataSource, Now))
            {
                var committedBookingFragment = JsonSerializer.Serialize(new
                {
                    bookingId = committed.Booking.Id,
                });
                var booking = await verifyCommit.Bookings.AsNoTracking()
                    .SingleAsync(row => row.Id == committed.Booking.Id);
                var action = await verifyCommit.BookingPendingActions.AsNoTracking()
                    .SingleAsync(row => row.Id == committed.Action.Id);
                var history = await verifyCommit.BookingStatusHistories.AsNoTracking()
                    .SingleAsync(row => row.BookingId == committed.Booking.Id);
                var outbox = await verifyCommit.OutboxEvents.AsNoTracking()
                    .SingleAsync(row => row.EventType == "booking.booking.cancelled"
                        && EF.Functions.JsonContains(row.Payload, committedBookingFragment));

                booking.Status.Should().Be(BookingStatus.CANCELLED);
                booking.CancellationReason.Should().Be(BookingCancellationReason.SCHEDULE_CHANGED);
                booking.RefundOverride.Should().BeTrue();
                action.ResolvedAction.Should().Be(BookingPendingActionResolved.REJECTED);
                history.Status.Should().Be(BookingStatus.CANCELLED);
                history.ActorUserId.Should().BeNull();
                using var payload = JsonDocument.Parse(outbox.Payload);
                outbox.Status.Should().Be(OutboxEventStatus.PENDING);
                payload.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(50_001);
                payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outbox.Id);
            }

            await using (var rollbackDb = Day22EventDatabase.CreateDbContext(dataSource, Now))
            {
                await rollbackDb.Database.ExecuteSqlRawAsync("""
                    CREATE OR REPLACE FUNCTION vietride_booking.fail_day23_outbox_insert()
                    RETURNS trigger AS $$
                    BEGIN
                        RAISE EXCEPTION 'force rollback after outbox insert';
                    END;
                    $$ LANGUAGE plpgsql;
                    CREATE TRIGGER trg_fail_day23_outbox_insert
                    AFTER INSERT ON vietride_booking.outbox_events
                    FOR EACH ROW EXECUTE FUNCTION vietride_booking.fail_day23_outbox_insert();
                    """);
                var act = () => CreateHandler(rollbackDb).Handle(
                    Command(rolledBack.Booking, rolledBack.Action, "REJECTED"),
                    CancellationToken.None);

                var failure = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
                failure.InnerException.Should().BeOfType<PostgresException>()
                    .Which.Message.Should().Contain("force rollback after outbox insert");
            }

            await using var verifyRollback = Day22EventDatabase.CreateDbContext(dataSource, Now);
            var rolledBackBookingFragment = JsonSerializer.Serialize(new
            {
                bookingId = rolledBack.Booking.Id,
            });
            (await verifyRollback.Bookings.AsNoTracking().SingleAsync(row => row.Id == rolledBack.Booking.Id))
                .Status.Should().Be(BookingStatus.CONFIRMED);
            (await verifyRollback.BookingPendingActions.AsNoTracking().SingleAsync(row => row.Id == rolledBack.Action.Id))
                .ResolvedAt.Should().BeNull();
            (await verifyRollback.BookingStatusHistories.AsNoTracking()
                .CountAsync(row => row.BookingId == rolledBack.Booking.Id)).Should().Be(0);
            (await verifyRollback.OutboxEvents.AsNoTracking()
                .CountAsync(row => EF.Functions.JsonContains(row.Payload, rolledBackBookingFragment)))
                .Should().Be(0);
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ResolverAndScheduleProducerUseActionThenBookingLockOrderWithoutDeadlock()
    {
        var databaseName = $"vr_d23_lock_order_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var aggregate = CreateAggregate(100_000);
            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, Now))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.Add(aggregate.Booking);
                seed.BookingPendingActions.Add(aggregate.Action);
                await seed.SaveChangesAsync();
            }

            var actionLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResolver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var resolverDb = Day22EventDatabase.CreateDbContext(dataSource, Now);
            await using var producerDb = Day22EventDatabase.CreateDbContext(dataSource, Now.AddMinutes(1));
            var realResolverActions = Day22EventDatabase.CreatePendingActionRepository(resolverDb);
            var resolverActions = Substitute.For<IBookingPendingActionRepository>();
            resolverActions.GetByIdForUpdateAsync(aggregate.Action.Id, Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var locked = await realResolverActions.GetByIdForUpdateAsync(
                        aggregate.Action.Id,
                        call.Arg<CancellationToken>());
                    actionLocked.TrySetResult();
                    await releaseResolver.Task;
                    return locked;
                });
            var resolver = new ResolvePendingActionCommandHandler(
                resolverActions,
                Day22EventDatabase.CreateBookingRepository(resolverDb),
                Day22EventDatabase.CreateStatusHistoryRepository(resolverDb),
                new IntegrationEventOutbox(new OutboxStore(resolverDb, new FixedClock(Now))),
                new EfUnitOfWork(resolverDb),
                new FixedClock(Now));
            var producer = new HandleScheduleChangeCommandHandler(
                Day22EventDatabase.CreateBookingRepository(producerDb),
                Day22EventDatabase.CreatePendingActionRepository(producerDb),
                new IntegrationEventOutbox(new OutboxStore(producerDb, new FixedClock(Now.AddMinutes(1)))),
                new EfUnitOfWork(producerDb),
                Substitute.For<IPendingActionRealertScheduler>(),
                new FixedClock(Now.AddMinutes(1)));

            var resolveTask = resolver.Handle(
                Command(aggregate.Booking, aggregate.Action, "ACCEPTED"),
                CancellationToken.None);
            await actionLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var produceTask = producer.Handle(
                new HandleScheduleChangeCommand(
                    Guid.NewGuid(),
                    Now.AddMinutes(1),
                    aggregate.Booking.TripId,
                    aggregate.Booking.OperatorId,
                    aggregate.Booking.TripCurrentDeparture!.Value,
                    aggregate.Booking.TripCurrentDeparture.Value.AddHours(3),
                    "MEDIUM"),
                CancellationToken.None);

            await WaitForRowLockAsync(connectionString);
            produceTask.IsCompleted.Should().BeFalse();
            releaseResolver.TrySetResult();
            await Task.WhenAll(resolveTask, produceTask).WaitAsync(TimeSpan.FromSeconds(10));

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, Now);
            var actions = await verify.BookingPendingActions.AsNoTracking()
                .Where(action => action.BookingId == aggregate.Booking.Id)
                .OrderBy(action => action.CreatedAt)
                .ToListAsync();
            actions.Should().HaveCount(2);
            actions.Should().ContainSingle(action => action.ResolvedAction == BookingPendingActionResolved.ACCEPTED);
            actions.Should().ContainSingle(action => action.ResolvedAt == null
                && action.Reason == BookingPendingActionReason.SCHEDULE_CHANGE);
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ResolvePendingActionCommandHandler CreateHandler(
        BookingDbContext db,
        IIntegrationEventOutbox? outbox = null)
        => new(
            Day22EventDatabase.CreatePendingActionRepository(db),
            Day22EventDatabase.CreateBookingRepository(db),
            Day22EventDatabase.CreateStatusHistoryRepository(db),
            outbox ?? new IntegrationEventOutbox(new OutboxStore(db, new FixedClock(Now))),
            new EfUnitOfWork(db),
            new FixedClock(Now));

    private static ResolvePendingActionCommand Command(
        BookingEntity booking,
        BookingPendingAction action,
        string resolution)
        => new(
            booking.Id,
            action.Id,
            booking.PassengerUserId,
            Guid.NewGuid().ToString("D"),
            resolution,
            null,
            []);

    private static (BookingEntity Booking, BookingPendingAction Action) CreateAggregate(long amount)
    {
        var booking = Day22EventDatabase.CreateBooking(
            Guid.NewGuid(),
            Guid.NewGuid(),
            confirmed: true,
            amount,
            Now.AddHours(10));
        var action = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.SCHEDULE_CHANGE,
            Now.AddHours(1),
            BookingPendingActionSeverity.MEDIUM,
            JsonSerializer.Serialize(new
            {
                sourceEventId = Guid.NewGuid(),
                oldDeparture = Now.AddHours(10),
                newDeparture = Now.AddHours(13),
                severity = "MEDIUM",
                initialDeadline = Now.AddHours(1),
                terminalDeadline = (DateTimeOffset?)null,
                refundBasisAmount = amount,
                refundPercent = 50,
                refundAmount = (long)Math.Round(amount * 0.5m, MidpointRounding.AwayFromZero),
            }));
        return (booking, action);
    }

    private static async Task WaitForRowLockAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND wait_event_type = 'Lock')
                """, connection);
            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Schedule producer never reached the pending-action row lock.");
    }
}
