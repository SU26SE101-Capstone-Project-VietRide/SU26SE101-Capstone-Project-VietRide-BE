using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.PendingActions;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.PendingActions;

public sealed class RouteChangePendingActionTests
{
    [Fact]
    public async Task PersistedDeadlineRetainsFrozenMetadataPrecision()
    {
        var databaseName = $"vr_d33_route_precision_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var occurredAt = DateTimeOffset.Parse("2026-07-23T01:00:00Z").AddTicks(7);
            var expectedDeadline = occurredAt.AddMinutes(30).AddTicks(-7);
            var booking = CreateConfirmedBooking(occurredAt);
            var sourceEventId = Guid.NewGuid();
            var candidateStopId = Guid.NewGuid();
            var scheduler = Substitute.For<IRouteChangeExpiryScheduler>();

            await using (var db = Day22EventDatabase.CreateDbContext(dataSource, occurredAt))
            {
                await db.Database.MigrateAsync();
                db.Bookings.Add(booking);
                await db.SaveChangesAsync();
                var handler = new CreateRouteChangePendingActionCommandHandler(
                    Day22EventDatabase.CreateBookingRepository(db),
                    Day22EventDatabase.CreatePendingActionRepository(db),
                    scheduler,
                    new EfUnitOfWork(db));
                (await handler.Handle(new CreateRouteChangePendingActionCommand(
                    sourceEventId,
                    occurredAt,
                    booking.TripId,
                    booking.OperatorId,
                    "IN_PROGRESS",
                    Guid.NewGuid(),
                    [
                        new RouteChangeAffectedBooking(
                            booking.Id,
                            [
                                new RouteChangeCandidateStop(
                                    candidateStopId,
                                    null,
                                    "Frozen stop",
                                    1,
                                    occurredAt.AddMinutes(10)),
                            ]),
                    ]), CancellationToken.None)).Should().Be(1);
            }

            await using var reload = Day22EventDatabase.CreateDbContext(dataSource, expectedDeadline);
            var action = await reload.BookingPendingActions.AsNoTracking().SingleAsync();
            action.Deadline.Should().Be(expectedDeadline);
            action.Deadline.Offset.Should().Be(TimeSpan.Zero);
            (action.Deadline.Ticks % TimeSpan.TicksPerMicrosecond).Should().Be(0);
            using var metadata = JsonDocument.Parse(action.Metadata!);
            metadata.RootElement.GetProperty("deadline").GetDateTimeOffset().Should().Be(action.Deadline);
            metadata.RootElement.GetProperty("candidateStops")[0].GetProperty("stopId").GetGuid()
                .Should().Be(candidateStopId);
            scheduler.Received(1).EnsureScheduled(action.Id, expectedDeadline.AddSeconds(1));
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task CreatesDeadlineMetadataAndUniqueActiveRow()
    {
        var occurredAt = DateTimeOffset.Parse("2026-07-23T01:00:00Z");
        var booking = CreateConfirmedBooking(occurredAt);
        var sourceEventId = Guid.NewGuid();
        var alternativeRouteId = Guid.NewGuid();
        var candidateStopId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var previous = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.SCHEDULE_CHANGE,
            occurredAt.AddHours(2));
        var bookings = Substitute.For<IBookingRepository>();
        var actions = Substitute.For<IBookingPendingActionRepository>();
        var scheduler = Substitute.For<IRouteChangeExpiryScheduler>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        BookingPendingAction? added = null;
        bookings.GetConfirmedByTripAsync(
                booking.TripId,
                booking.OperatorId,
                Arg.Any<CancellationToken>())
            .Returns([booking]);
        actions.GetActiveByBookingIdAsync(booking.Id, Arg.Any<CancellationToken>())
            .Returns(previous);
        actions.GetByBookingAndSourceEventAsync(
                booking.Id,
                sourceEventId,
                Arg.Any<CancellationToken>())
            .Returns([]);
        actions.AddAsync(
                Arg.Do<BookingPendingAction>(action => added = action),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<BookingPendingAction>());
        unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<Task<int>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task<int>>>()());
        var handler = new CreateRouteChangePendingActionCommandHandler(
            bookings,
            actions,
            scheduler,
            unitOfWork);
        var command = new CreateRouteChangePendingActionCommand(
            sourceEventId,
            occurredAt,
            booking.TripId,
            booking.OperatorId,
            "IN_PROGRESS",
            alternativeRouteId,
            [
                new RouteChangeAffectedBooking(
                    booking.Id,
                    [
                        new RouteChangeCandidateStop(
                            candidateStopId,
                            null,
                            "Stop A",
                            1,
                            occurredAt.AddMinutes(10)),
                        new RouteChangeCandidateStop(
                            null,
                            destinationStationId,
                            "Destination",
                            2,
                            occurredAt.AddMinutes(25)),
                    ]),
            ]);

        (await handler.Handle(command, CancellationToken.None)).Should().Be(1);

        previous.ResolvedAction.Should().Be(BookingPendingActionResolved.SUPERSEDED);
        added.Should().NotBeNull();
        added!.Reason.Should().Be(BookingPendingActionReason.ROUTE_CHANGE);
        added.Deadline.Should().Be(occurredAt.AddMinutes(30));
        using var metadata = JsonDocument.Parse(added.Metadata!);
        metadata.RootElement.GetProperty("sourceEventId").GetGuid().Should().Be(sourceEventId);
        metadata.RootElement.GetProperty("tripStatus").GetString().Should().Be("IN_PROGRESS");
        metadata.RootElement.GetProperty("alternativeRouteId").GetGuid().Should().Be(alternativeRouteId);
        metadata.RootElement.GetProperty("candidateStops")[0].GetProperty("stopId").GetGuid()
            .Should().Be(candidateStopId);
        metadata.RootElement.GetProperty("candidateStops")[1].GetProperty("stationId").GetGuid()
            .Should().Be(destinationStationId);
        scheduler.Received(1).EnsureScheduled(added.Id, added.Deadline.AddSeconds(1));

        actions.GetByBookingAndSourceEventAsync(
                booking.Id,
                sourceEventId,
                Arg.Any<CancellationToken>())
            .Returns([added]);
        (await handler.Handle(command, CancellationToken.None)).Should().Be(0);
        await actions.Received(1).AddAsync(
            Arg.Any<BookingPendingAction>(),
            Arg.Any<CancellationToken>());
    }

    private static BookingEntity CreateConfirmedBooking(DateTimeOffset now)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            tripSnapshotDeparture: now.AddHours(3));
        booking.Confirm(now.AddHours(-1));
        return booking;
    }
}
