using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.HandleTripCancelled;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingCancelledEvent = VietRide.Booking.Application.Events.BookingCancelledIntegrationEvent;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class TripCancelledByOperatorConsumerTests
{
    private static readonly DateTimeOffset CancelledAt =
        DateTimeOffset.Parse("2026-07-23T02:00:00Z");

    [Fact]
    public async Task CancelsActiveBookingsAndEmitsCanonicalEvent()
    {
        await WithDatabaseAsync(async (dataSource, tripId, operatorId) =>
        {
            var pending = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: false, 125_000);
            var confirmed = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: true, 225_000);
            await SeedAsync(dataSource, pending, confirmed);

            await using (var consume = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt))
            {
                await CreateDatabaseBackedHandler(consume).HandleAsync(
                    CreateEvent(tripId, operatorId),
                    CancellationToken.None);
            }

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
            var bookings = await verify.Bookings.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
            bookings.Should().OnlyContain(row =>
                row.Status == BookingStatus.CANCELLED
                && row.CancellationReason == BookingCancellationReason.OPERATOR_CANCELLED_TRIP
                && row.RefundOverride);

            var outbox = await verify.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == BookingCancelledEvent.EventTypeValue)
                .ToArrayAsync();
            outbox.Should().HaveCount(2);
            var refunds = outbox.Select(row =>
            {
                using var payload = JsonDocument.Parse(row.Payload);
                payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
                payload.RootElement.GetProperty("cancellationReason").GetString().Should()
                    .Be(BookingCancellationReason.OPERATOR_CANCELLED_TRIP.ToString());
                return payload.RootElement.GetProperty("refundAmount").GetInt64();
            });
            refunds.Should().BeEquivalentTo([0L, 225_000L]);
        });
    }

    [Fact]
    public async Task ReplayIsDedupedAndPendingPaymentRefundIsZero()
    {
        await WithDatabaseAsync(async (dataSource, tripId, operatorId) =>
        {
            var pending = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: false, 125_000);
            await SeedAsync(dataSource, pending);
            var integrationEvent = CreateEvent(tripId, operatorId);

            await using (var first = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt))
            {
                await CreateDatabaseBackedHandler(first).HandleAsync(integrationEvent, CancellationToken.None);
            }

            await using (var replay = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt))
            {
                await CreateDatabaseBackedHandler(replay).HandleAsync(integrationEvent, CancellationToken.None);
            }

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
            (await verify.Bookings.AsNoTracking().SingleAsync()).Status.Should().Be(BookingStatus.CANCELLED);
            var outbox = await verify.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.EventType == BookingCancelledEvent.EventTypeValue);
            using var payload = JsonDocument.Parse(outbox.Payload);
            payload.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(0);
            (await verify.BookingStatusHistories.AsNoTracking().CountAsync()).Should().Be(1);
        });
    }

    [Fact]
    public async Task OutboxUsesSameTransactionAndNullProcessedAt()
    {
        await WithDatabaseAsync(async (dataSource, tripId, operatorId) =>
        {
            var booking = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: true, 225_000);
            await SeedAsync(dataSource, booking);

            await using (var consume = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt))
            {
                var realBookings = Day22EventDatabase.CreateBookingRepository(consume);
                var histories = Substitute.For<IBookingStatusHistoryRepository>();
                histories.AddAsync(
                        Arg.Any<VietRide.Booking.Domain.Entities.BookingStatusHistory>(),
                        Arg.Any<CancellationToken>())
                    .Returns(_ => throw new InvalidOperationException("Force transaction rollback."));
                var commandHandler = new HandleTripCancelledCommandHandler(
                    realBookings,
                    histories,
                    new IntegrationEventOutbox(new OutboxStore(consume, new FixedClock(CancelledAt))),
                    new EfUnitOfWork(consume),
                    new FixedClock(CancelledAt));

                var act = () => commandHandler.Handle(
                    ToCommand(CreateEvent(tripId, operatorId)),
                    CancellationToken.None);
                await act.Should().ThrowAsync<InvalidOperationException>();
            }

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
            (await verify.Bookings.AsNoTracking().SingleAsync()).Status.Should().Be(BookingStatus.CONFIRMED);
            (await verify.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessedAtNullRoutingExchangeMessageIdRestartAndDedupe()
    {
        await WithDatabaseAsync(async (dataSource, tripId, operatorId) =>
        {
            var booking = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: true, 225_000);
            await SeedAsync(dataSource, booking);
            var integrationEvent = CreateEvent(tripId, operatorId);

            await using (var first = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt))
            {
                await CreateDatabaseBackedHandler(first).HandleAsync(integrationEvent, CancellationToken.None);
            }

            await using (var restarted = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt))
            {
                await CreateDatabaseBackedHandler(restarted).HandleAsync(integrationEvent, CancellationToken.None);
            }

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
            var row = await verify.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.EventType == BookingCancelledEvent.EventTypeValue);
            row.EventType.Should().Be("booking.booking.cancelled");
            row.Status.Should().Be(OutboxEventStatus.PENDING);
            row.PublishedAt.Should().BeNull();
            using var payload = JsonDocument.Parse(row.Payload);
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
            (await verify.BookingStatusHistories.AsNoTracking().CountAsync()).Should().Be(1);
        });
    }

    private static TripCancelledByOperatorIntegrationEvent CreateEvent(Guid tripId, Guid operatorId)
        => new()
        {
            EventId = Guid.NewGuid(),
            OccurredAt = CancelledAt.UtcDateTime,
            TripId = tripId,
            OperatorId = operatorId,
            CancelledAt = CancelledAt,
            CancelReason = "Vehicle issue",
        };

    private static HandleTripCancelledCommand ToCommand(
        TripCancelledByOperatorIntegrationEvent integrationEvent)
        => new(
            integrationEvent.EventId,
            new DateTimeOffset(integrationEvent.OccurredAt),
            integrationEvent.TripId,
            integrationEvent.OperatorId,
            integrationEvent.CancelledAt,
            integrationEvent.CancelReason,
            AllowOperatorReason: true);

    private static IIntegrationEventHandler<TripCancelledByOperatorIntegrationEvent>
        CreateDatabaseBackedHandler(BookingDbContext db)
    {
        var realBookings = Day22EventDatabase.CreateBookingRepository(db);
        var bookings = Substitute.For<IBookingRepository>();
        bookings.AcquireEventLockAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => realBookings.AcquireEventLockAsync(
                call.Arg<Guid>(),
                call.Arg<CancellationToken>()));
        bookings.GetCancellableByTripAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call => realBookings.GetCancellableByTripAsync(
                call.ArgAt<Guid>(0),
                call.ArgAt<Guid>(1),
                call.ArgAt<CancellationToken>(2)));
        bookings.When(repository => repository.Update(Arg.Any<BookingEntity>()))
            .Do(call => realBookings.Update(call.Arg<BookingEntity>()));

        var commandHandler = new HandleTripCancelledCommandHandler(
            bookings,
            Day22EventDatabase.CreateStatusHistoryRepository(db),
            new IntegrationEventOutbox(new OutboxStore(db, new FixedClock(CancelledAt))),
            new EfUnitOfWork(db),
            new FixedClock(CancelledAt));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<HandleTripCancelledCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => commandHandler.Handle(
                call.Arg<HandleTripCancelledCommand>(),
                call.Arg<CancellationToken>()));

        var type = typeof(TripCancelledByOperatorIntegrationEvent).Assembly.GetType(
            "VietRide.Booking.Infrastructure.Messaging.TripCancelledByOperatorIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<TripCancelledByOperatorIntegrationEvent>)
            Activator.CreateInstance(type, mediator)!;
    }

    private static async Task SeedAsync(
        Npgsql.NpgsqlDataSource dataSource,
        params BookingEntity[] bookings)
    {
        await using var seed = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
        await seed.Database.MigrateAsync();
        seed.Bookings.AddRange(bookings);
        await seed.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(
        Func<Npgsql.NpgsqlDataSource, Guid, Guid, Task> test)
    {
        var databaseName = $"vr_d33_cancel_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);
        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            await test(dataSource, Guid.NewGuid(), Guid.NewGuid());
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }
}
