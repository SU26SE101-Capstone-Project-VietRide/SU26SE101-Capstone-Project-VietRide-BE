using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.HandleTripCancelled;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;
using BookingCancelledEvent = VietRide.Booking.Application.Events.BookingCancelledIntegrationEvent;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class TripCancelledIntegrationEventHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset CancelledAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z");

    [Fact]
    public async Task ConcurrentDeliveriesSerializeOnPostgresLockAndCancelExactlyOnce()
    {
        var databaseName = $"vr_b22_cancel_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var tripId = Guid.NewGuid();
            var operatorId = Guid.NewGuid();
            var booking = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: true, 225_000);
            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.Add(booking);
                await seed.SaveChangesAsync();
            }

            var integrationEvent = new TripCancelledIntegrationEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAt = CancelledAt.UtcDateTime,
                TripId = tripId,
                OperatorId = operatorId,
                CancelledAt = CancelledAt,
                CancelReason = HandleTripCancelledCommandHandler.DriverScheduleDayRemovedReason,
            };
            var firstHasLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var firstDb = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
            await using var secondDb = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
            var firstHandler = CreateDatabaseBackedHandler(
                firstDb,
                async () =>
                {
                    firstHasLock.TrySetResult();
                    await releaseFirst.Task;
                });
            var secondHandler = CreateDatabaseBackedHandler(secondDb);

            var first = firstHandler.HandleAsync(integrationEvent, CancellationToken.None);
            await firstHasLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = secondHandler.HandleAsync(integrationEvent, CancellationToken.None);
            try
            {
                await Day22EventDatabase.WaitForWaitingAdvisoryLockAsync(connectionString);
            }
            finally
            {
                releaseFirst.TrySetResult();
            }

            await Task.WhenAll(first, second);

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, CancelledAt);
            var persisted = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id);
            persisted.Status.Should().Be(BookingStatus.CANCELLED);
            persisted.CancellationReason.Should().Be(BookingCancellationReason.OPERATOR_CANCELLED_TRIP);
            persisted.RefundOverride.Should().BeTrue();
            persisted.CancelledAt.Should().Be(CancelledAt);

            var history = await verify.BookingStatusHistories.AsNoTracking().SingleAsync();
            history.BookingId.Should().Be(booking.Id);
            history.Status.Should().Be(BookingStatus.CANCELLED);
            history.OccurredAt.Should().Be(CancelledAt);
            history.Source.Should().Be(BookingStatusHistorySource.CancelBooking);
            history.ActorUserId.Should().Be(operatorId);
            history.ReasonCode.Should().Be(BookingCancellationReason.OPERATOR_CANCELLED_TRIP.ToString());

            var outbox = await verify.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.EventType == BookingCancelledEvent.EventTypeValue);
            using var payload = JsonDocument.Parse(outbox.Payload);
            payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
                ["bookingId", "bookingCode", "userId", "refundAmount", "refundOverride", "cancellationReason", "ticketCodes", "ticketCount"]);
            payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
            payload.RootElement.GetProperty("bookingCode").GetString().Should().Be(booking.BookingCode.Value);
            payload.RootElement.GetProperty("userId").GetGuid().Should().Be(booking.PassengerUserId);
            payload.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(225_000);
            payload.RootElement.GetProperty("refundOverride").GetBoolean().Should().BeTrue();
            payload.RootElement.GetProperty("cancellationReason").GetString().Should()
                .Be(BookingCancellationReason.OPERATOR_CANCELLED_TRIP.ToString());
            payload.RootElement.GetProperty("ticketCount").GetInt32().Should().Be(0);
            payload.RootElement.GetProperty("ticketCodes").GetArrayLength().Should().Be(0);
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ExactDayRemovalContractMapsOperatorTimestampsAndReason()
    {
        var mediator = Substitute.For<IMediator>();
        HandleTripCancelledCommand? captured = null;
        mediator.Send(Arg.Do<HandleTripCancelledCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(1);
        var json = """
            {
              "eventId":"11111111-1111-1111-1111-111111111111",
              "occurredAt":"2026-07-15T01:00:00Z",
              "tripId":"22222222-2222-2222-2222-222222222222",
              "operatorId":"33333333-3333-3333-3333-333333333333",
              "cancelledAt":"2026-07-15T01:00:00Z",
              "cancelReason":"DRIVER_SCHEDULE_DAY_REMOVED"
            }
            """;
        var integrationEvent = JsonSerializer.Deserialize<TripCancelledIntegrationEvent>(json, JsonOptions)!;

        await CreateHandler(mediator).HandleAsync(integrationEvent, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OperatorId.Should().Be(integrationEvent.OperatorId);
        captured.OccurredAt.Should().Be(integrationEvent.CancelledAt);
        captured.CancelledAt.Should().Be(integrationEvent.CancelledAt);
        captured.CancelReason.Should().Be("DRIVER_SCHEDULE_DAY_REMOVED");
    }

    [Fact]
    public void ContractRejectsExtraField()
    {
        var json = """
            {
              "eventId":"11111111-1111-1111-1111-111111111111",
              "occurredAt":"2026-07-15T01:00:00Z",
              "tripId":"22222222-2222-2222-2222-222222222222",
              "operatorId":"33333333-3333-3333-3333-333333333333",
              "cancelledAt":"2026-07-15T01:00:00Z",
              "cancelReason":"DRIVER_SCHEDULE_DAY_REMOVED",
              "unexpected":true
            }
            """;

        var act = () => JsonSerializer.Deserialize<TripCancelledIntegrationEvent>(json, JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("OTHER_REASON", 0)]
    [InlineData("DRIVER_SCHEDULE_DAY_REMOVED", 1)]
    public async Task HandlerRejectsWrongReasonAndTimestamp(string reason, int timestampOffsetSeconds)
    {
        var mediator = Substitute.For<IMediator>();
        var occurredAt = new DateTime(2026, 7, 15, 1, 0, 0, DateTimeKind.Utc);
        var integrationEvent = new TripCancelledIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
            TripId = Guid.NewGuid(),
            OperatorId = Guid.NewGuid(),
            CancelledAt = new DateTimeOffset(occurredAt).AddSeconds(timestampOffsetSeconds),
            CancelReason = reason,
        };

        var act = () => CreateHandler(mediator).HandleAsync(integrationEvent, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        await mediator.DidNotReceiveWithAnyArgs().Send(default!, default);
    }

    private static IIntegrationEventHandler<TripCancelledIntegrationEvent> CreateDatabaseBackedHandler(
        BookingDbContext db,
        Func<Task>? afterLock = null)
    {
        var realBookings = Day22EventDatabase.CreateBookingRepository(db);
        var bookings = Substitute.For<IBookingRepository>();
        bookings.AcquireEventLockAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await realBookings.AcquireEventLockAsync(call.Arg<Guid>(), call.Arg<CancellationToken>());
                if (afterLock is not null)
                {
                    await afterLock();
                }
            });
        bookings.GetCancellableByTripAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => realBookings.GetCancellableByTripAsync(
                call.ArgAt<Guid>(0), call.ArgAt<Guid>(1), call.ArgAt<CancellationToken>(2)));
        bookings.When(repository => repository.Update(Arg.Any<BookingEntity>()))
            .Do(call => realBookings.Update(call.Arg<BookingEntity>()));

        var clock = new FixedClock(CancelledAt);
        var commandHandler = new HandleTripCancelledCommandHandler(
            bookings,
            Day22EventDatabase.CreateStatusHistoryRepository(db),
            new IntegrationEventOutbox(new OutboxStore(db, clock)),
            new EfUnitOfWork(db));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<HandleTripCancelledCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => commandHandler.Handle(
                call.Arg<HandleTripCancelledCommand>(), call.Arg<CancellationToken>()));
        return CreateHandler(mediator);
    }

    private static IIntegrationEventHandler<TripCancelledIntegrationEvent> CreateHandler(IMediator mediator)
    {
        var type = typeof(TripCancelledIntegrationEvent).Assembly.GetType(
            "VietRide.Booking.Infrastructure.Messaging.TripCancelledIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<TripCancelledIntegrationEvent>)Activator.CreateInstance(type, mediator)!;
    }
}
