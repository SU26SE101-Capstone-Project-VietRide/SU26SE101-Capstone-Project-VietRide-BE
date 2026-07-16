using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class TripScheduleChangedIntegrationEventHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset NotifiedAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z");

    [Fact]
    public async Task ConcurrentMediumDeliveriesSerializeOnPostgresLockAndPersistOneDurableResult()
    {
        var databaseName = $"vr_b22_sched_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var tripId = Guid.NewGuid();
            var operatorId = Guid.NewGuid();
            var booking = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: true, 175_000);
            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.Add(booking);
                await seed.SaveChangesAsync();
            }

            var integrationEvent = CreateMediumEvent(tripId, operatorId);
            var scheduler = Substitute.For<IPendingActionRealertScheduler>();
            var firstHasLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var firstDb = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt);
            await using var secondDb = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt);
            var firstHandler = CreateDatabaseBackedHandler(
                firstDb,
                scheduler,
                async () =>
                {
                    firstHasLock.TrySetResult();
                    await releaseFirst.Task;
                });
            var secondHandler = CreateDatabaseBackedHandler(secondDb, scheduler);

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

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt);
            var action = await verify.BookingPendingActions.AsNoTracking().SingleAsync();
            var outbox = await verify.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.EventType == BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue);

            action.BookingId.Should().Be(booking.Id);
            action.Reason.Should().Be(BookingPendingActionReason.SCHEDULE_CHANGE);
            action.Severity.Should().Be(BookingPendingActionSeverity.MEDIUM);
            action.Deadline.Should().Be(NotifiedAt.AddHours(24));
            using (var metadata = JsonDocument.Parse(action.Metadata!))
            {
                metadata.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
                    ["sourceEventId", "oldDeparture", "newDeparture", "severity"]);
                metadata.RootElement.GetProperty("sourceEventId").GetGuid().Should().Be(integrationEvent.EventId);
                metadata.RootElement.GetProperty("oldDeparture").GetDateTimeOffset().Should()
                    .Be(integrationEvent.OldDeparture);
                metadata.RootElement.GetProperty("newDeparture").GetDateTimeOffset().Should()
                    .Be(integrationEvent.NewDeparture);
                metadata.RootElement.GetProperty("severity").GetString().Should().Be("MEDIUM");
            }

            using (var payload = JsonDocument.Parse(outbox.Payload))
            {
                payload.RootElement.GetProperty("eventId").GetGuid().Should()
                    .Be(DeriveRequiredEventId(integrationEvent.EventId, booking.Id, "MEDIUM"));
                payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
                payload.RootElement.GetProperty("pendingActionId").GetGuid().Should().Be(action.Id);
                payload.RootElement.GetProperty("deadline").GetDateTimeOffset().Should().Be(action.Deadline);
                payload.RootElement.GetProperty("severity").GetString().Should().Be("MEDIUM");
            }

            scheduler.Received(2).EnsureScheduled(action.Id, NotifiedAt.AddHours(2));
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task SchedulerFailureAfterCommitPropagatesAndReplayRepairsWithoutDurableDuplicates()
    {
        var databaseName = $"vr_b22_sched_replay_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var tripId = Guid.NewGuid();
            var operatorId = Guid.NewGuid();
            var booking = Day22EventDatabase.CreateBooking(tripId, operatorId, confirmed: true, 180_000);
            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.Add(booking);
                await seed.SaveChangesAsync();
            }

            var integrationEvent = CreateMediumEvent(tripId, operatorId);
            var failingScheduler = Substitute.For<IPendingActionRealertScheduler>();
            failingScheduler.When(scheduler => scheduler.EnsureScheduled(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()))
                .Do(_ => throw new InvalidOperationException("hangfire unavailable"));

            await using (var firstDb = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt))
            {
                var act = () => CreateDatabaseBackedHandler(firstDb, failingScheduler)
                    .HandleAsync(integrationEvent, CancellationToken.None);
                await act.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("hangfire unavailable");
            }

            Guid pendingActionId;
            await using (var afterFailure = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt))
            {
                pendingActionId = (await afterFailure.BookingPendingActions.AsNoTracking().SingleAsync()).Id;
                (await afterFailure.OutboxEvents.AsNoTracking()
                    .CountAsync(row => row.EventType == BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue))
                    .Should().Be(1);
            }

            var repairScheduler = Substitute.For<IPendingActionRealertScheduler>();
            await using (var replayDb = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt))
            {
                await CreateDatabaseBackedHandler(replayDb, repairScheduler)
                    .HandleAsync(integrationEvent, CancellationToken.None);
            }

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, NotifiedAt);
            (await verify.BookingPendingActions.AsNoTracking().CountAsync()).Should().Be(1);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue))
                .Should().Be(1);
            repairScheduler.Received(1).EnsureScheduled(pendingActionId, NotifiedAt.AddHours(2));
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task StrictContractMapsEveryFieldToCommand()
    {
        var mediator = Substitute.For<IMediator>();
        HandleScheduleChangeCommand? captured = null;
        mediator.Send(Arg.Do<HandleScheduleChangeCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(1);
        var json = """
            {
              "eventId":"11111111-1111-1111-1111-111111111111",
              "occurredAt":"2026-07-15T01:00:00Z",
              "tripId":"22222222-2222-2222-2222-222222222222",
              "operatorId":"33333333-3333-3333-3333-333333333333",
              "oldDeparture":"2026-07-15T03:00:00Z",
              "newDeparture":"2026-07-15T05:00:00Z",
              "severity":"MINOR"
            }
            """;
        var integrationEvent = JsonSerializer.Deserialize<TripScheduleChangedIntegrationEvent>(json, JsonOptions)!;

        await CreateHandler(mediator).HandleAsync(integrationEvent, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.EventId.Should().Be(integrationEvent.EventId);
        captured.OccurredAt.Should().Be(new DateTimeOffset(integrationEvent.OccurredAt));
        captured.TripId.Should().Be(integrationEvent.TripId);
        captured.OperatorId.Should().Be(integrationEvent.OperatorId);
        captured.OldDeparture.Should().Be(integrationEvent.OldDeparture);
        captured.NewDeparture.Should().Be(integrationEvent.NewDeparture);
        captured.Severity.Should().Be("MINOR");
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
              "oldDeparture":"2026-07-15T03:00:00Z",
              "newDeparture":"2026-07-15T05:00:00Z",
              "severity":"MINOR",
              "extra":true
            }
            """;

        var act = () => JsonSerializer.Deserialize<TripScheduleChangedIntegrationEvent>(json, JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ContractRejectsMissingSeverity()
    {
        var json = """
            {
              "eventId":"11111111-1111-1111-1111-111111111111",
              "occurredAt":"2026-07-15T01:00:00Z",
              "tripId":"22222222-2222-2222-2222-222222222222",
              "operatorId":"33333333-3333-3333-3333-333333333333",
              "oldDeparture":"2026-07-15T03:00:00Z",
              "newDeparture":"2026-07-15T05:00:00Z"
            }
            """;

        var act = () => JsonSerializer.Deserialize<TripScheduleChangedIntegrationEvent>(json, JsonOptions);

        act.Should().Throw<JsonException>();
    }

    private static TripScheduleChangedIntegrationEvent CreateMediumEvent(Guid tripId, Guid operatorId)
        => new()
        {
            EventId = Guid.NewGuid(),
            OccurredAt = NotifiedAt.UtcDateTime,
            TripId = tripId,
            OperatorId = operatorId,
            OldDeparture = NotifiedAt.AddHours(27),
            NewDeparture = NotifiedAt.AddHours(30),
            Severity = "MEDIUM",
        };

    private static IIntegrationEventHandler<TripScheduleChangedIntegrationEvent> CreateDatabaseBackedHandler(
        BookingDbContext db,
        IPendingActionRealertScheduler scheduler,
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
        bookings.GetConfirmedByTripAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => realBookings.GetConfirmedByTripAsync(
                call.ArgAt<Guid>(0), call.ArgAt<Guid>(1), call.ArgAt<CancellationToken>(2)));
        bookings.HasOutboxEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => realBookings.HasOutboxEventAsync(
                call.ArgAt<string>(0), call.ArgAt<Guid>(1), call.ArgAt<CancellationToken>(2)));

        var clock = new FixedClock(NotifiedAt.AddMinutes(5));
        var commandHandler = new HandleScheduleChangeCommandHandler(
            bookings,
            Day22EventDatabase.CreatePendingActionRepository(db),
            new IntegrationEventOutbox(new OutboxStore(db, clock)),
            new EfUnitOfWork(db),
            scheduler,
            clock);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<HandleScheduleChangeCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => commandHandler.Handle(
                call.Arg<HandleScheduleChangeCommand>(), call.Arg<CancellationToken>()));
        return CreateHandler(mediator);
    }

    private static Guid DeriveRequiredEventId(Guid sourceEventId, Guid bookingId, string severity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"booking.schedule-change:{sourceEventId:N}:{bookingId:N}:{severity}"));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static IIntegrationEventHandler<TripScheduleChangedIntegrationEvent> CreateHandler(IMediator mediator)
    {
        var type = typeof(TripScheduleChangedIntegrationEvent).Assembly.GetType(
            "VietRide.Booking.Infrastructure.Messaging.TripScheduleChangedIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<TripScheduleChangedIntegrationEvent>)Activator.CreateInstance(type, mediator)!;
    }
}

internal static class Day22EventDatabase
{
    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<OutboxEventStatus>(
            $"{BookingDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        BookingDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    public static BookingDbContext CreateDbContext(NpgsqlDataSource dataSource, DateTimeOffset now)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
            .Options;
        return new BookingDbContext(options, new FixedClock(now));
    }

    public static IBookingRepository CreateBookingRepository(BookingDbContext db)
        => CreateRepository<IBookingRepository>(
            "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingRepository", db);

    public static IBookingPendingActionRepository CreatePendingActionRepository(BookingDbContext db)
        => CreateRepository<IBookingPendingActionRepository>(
            "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingPendingActionRepository", db);

    public static IBookingStatusHistoryRepository CreateStatusHistoryRepository(BookingDbContext db)
        => CreateRepository<IBookingStatusHistoryRepository>(
            "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingStatusHistoryRepository", db);

    public static BookingEntity CreateBooking(
        Guid tripId,
        Guid operatorId,
        bool confirmed,
        long totalAmount)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow),
            Guid.NewGuid(),
            tripId,
            operatorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(totalAmount),
            Money.Zero,
            Money.FromRaw(totalAmount));
        if (confirmed)
        {
            booking.Confirm(DateTimeOffset.Parse("2026-07-15T00:00:00Z"));
        }

        return booking;
    }

    public static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_BOOKING_TEST_CONNECTION_STRING");
        var template = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(template.Replace(
            "{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    public static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);", connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task WaitForWaitingAdvisoryLockAsync(string connectionString)
    {
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_locks AS locks
                    INNER JOIN pg_stat_activity AS activity ON activity.pid = locks.pid
                    WHERE activity.datname = @database_name
                      AND locks.locktype = 'advisory'
                      AND locks.granted = FALSE)
                """, observer);
            command.Parameters.AddWithValue("database_name", databaseName!);
            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The duplicate delivery never reached the PostgreSQL advisory-lock wait state.");
    }

    private static T CreateRepository<T>(string typeName, BookingDbContext db)
        => (T)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(typeName, throwOnError: true)!, db)!;
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}
