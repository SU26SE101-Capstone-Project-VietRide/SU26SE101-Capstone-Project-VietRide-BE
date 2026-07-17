using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Booking.IntegrationTests;

public sealed class Day23ScheduleDeadlinePrecisionIntegrationTests
{
    [Fact]
    public async Task PersistedCanonicalDeadlineAllowsEqualityAndRejectsStrictlyAfter()
    {
        var databaseName = $"vr_d23_deadline_precision_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var occurredAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z").AddTicks(7);
            var oldDeparture = occurredAt.AddHours(27);
            var newDeparture = occurredAt.AddHours(30);
            var expectedDeadline = occurredAt.AddHours(24).AddTicks(-7);
            var tripId = Guid.NewGuid();
            var operatorId = Guid.NewGuid();
            var exactBooking = Day22EventDatabase.CreateBooking(
                tripId, operatorId, confirmed: true, 100_000, oldDeparture);
            var afterBooking = Day22EventDatabase.CreateBooking(
                tripId, operatorId, confirmed: true, 200_000, oldDeparture);

            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, occurredAt))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.AddRange(exactBooking, afterBooking);
                await seed.SaveChangesAsync();
            }

            var autoAcceptScheduler = Substitute.For<IScheduleChangeAutoAcceptScheduler>();
            await using (var producerDb = Day22EventDatabase.CreateDbContext(dataSource, occurredAt.AddMinutes(5)))
            {
                var clock = new FixedClock(occurredAt.AddMinutes(5));
                var producer = new HandleScheduleChangeCommandHandler(
                    Day22EventDatabase.CreateBookingRepository(producerDb),
                    Day22EventDatabase.CreatePendingActionRepository(producerDb),
                    new IntegrationEventOutbox(new OutboxStore(producerDb, clock)),
                    new EfUnitOfWork(producerDb),
                    Substitute.For<IPendingActionRealertScheduler>(),
                    clock,
                    autoAcceptScheduler);

                (await producer.Handle(
                    new HandleScheduleChangeCommand(
                        Guid.NewGuid(),
                        occurredAt,
                        tripId,
                        operatorId,
                        oldDeparture,
                        newDeparture,
                        "MEDIUM"),
                    CancellationToken.None)).Should().Be(2);
            }

            Guid exactActionId;
            Guid afterActionId;
            await using (var reload = Day22EventDatabase.CreateDbContext(dataSource, expectedDeadline))
            {
                var actions = await reload.BookingPendingActions.AsNoTracking()
                    .OrderBy(action => action.BookingId)
                    .ToListAsync();
                actions.Should().HaveCount(2);
                actions.Should().OnlyContain(action => action.Deadline == expectedDeadline);
                actions.Should().OnlyContain(action => action.Deadline.Ticks % TimeSpan.TicksPerMicrosecond == 0);

                foreach (var action in actions)
                {
                    using var metadata = JsonDocument.Parse(action.Metadata!);
                    metadata.RootElement.GetProperty("initialDeadline").GetDateTimeOffset().Should()
                        .Be(action.Deadline);
                    metadata.RootElement.GetProperty("terminalDeadline").ValueKind.Should()
                        .Be(JsonValueKind.Null);
                }

                var requiredDeadlines = await reload.OutboxEvents.AsNoTracking()
                    .Where(row => row.EventType == BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue)
                    .Select(row => row.Payload)
                    .ToListAsync();
                requiredDeadlines.Should().HaveCount(2);
                requiredDeadlines.Select(payload =>
                    JsonDocument.Parse(payload).RootElement.GetProperty("deadline").GetDateTimeOffset())
                    .Should().OnlyContain(deadline => deadline == expectedDeadline);

                exactActionId = actions.Single(action => action.BookingId == exactBooking.Id).Id;
                afterActionId = actions.Single(action => action.BookingId == afterBooking.Id).Id;
            }

            autoAcceptScheduler.Received(2).EnsureScheduled(
                Arg.Any<Guid>(),
                expectedDeadline.AddSeconds(1));

            await using (var exactDb = Day22EventDatabase.CreateDbContext(dataSource, expectedDeadline))
            {
                var result = await CreateResolver(exactDb, expectedDeadline).Handle(
                    ResolveCommand(exactBooking.Id, exactActionId, exactBooking.PassengerUserId),
                    CancellationToken.None);
                result.ResolvedAction.Should().Be("ACCEPTED");
                result.ResolvedAt.Should().Be(expectedDeadline);
            }

            await using (var afterDb = Day22EventDatabase.CreateDbContext(dataSource, expectedDeadline.AddTicks(1)))
            {
                var act = () => CreateResolver(afterDb, expectedDeadline.AddTicks(1)).Handle(
                    ResolveCommand(afterBooking.Id, afterActionId, afterBooking.PassengerUserId),
                    CancellationToken.None);

                var conflict = (await act.Should().ThrowAsync<CodedConflictException>()).Which;
                conflict.ErrorCode.Should().Be("BOOKING_PENDING_ACTION_EXPIRED");
            }

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, expectedDeadline);
            (await verify.BookingPendingActions.AsNoTracking().SingleAsync(action => action.Id == exactActionId))
                .ResolvedAction.Should().Be(BookingPendingActionResolved.ACCEPTED);
            (await verify.BookingPendingActions.AsNoTracking().SingleAsync(action => action.Id == afterActionId))
                .ResolvedAt.Should().BeNull();
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ResolvePendingActionCommandHandler CreateResolver(
        BookingDbContext db,
        DateTimeOffset now)
        => new(
            Day22EventDatabase.CreatePendingActionRepository(db),
            Day22EventDatabase.CreateBookingRepository(db),
            Day22EventDatabase.CreateStatusHistoryRepository(db),
            new IntegrationEventOutbox(new OutboxStore(db, new FixedClock(now))),
            new EfUnitOfWork(db),
            new FixedClock(now));

    private static ResolvePendingActionCommand ResolveCommand(
        Guid bookingId,
        Guid actionId,
        Guid passengerUserId)
        => new(
            bookingId,
            actionId,
            passengerUserId,
            Guid.NewGuid().ToString("D"),
            "ACCEPTED",
            null,
            []);
}
