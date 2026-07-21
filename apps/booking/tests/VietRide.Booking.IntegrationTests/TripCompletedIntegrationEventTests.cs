using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.TripEvents;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class TripCompletedIntegrationEventTests
{
    [Fact]
    public async Task Delivery_TransitionsOnlyEligibleRows_WritesHistory_AndReplayIsNoOp()
    {
        var databaseName = $"vietride_booking_trip_completed_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var db = CreateDbContext(dataSource);
            await db.Database.MigrateAsync();

            var tripId = Guid.NewGuid();
            var otherTripId = Guid.NewGuid();
            var completedAt = new DateTimeOffset(2026, 7, 14, 18, 30, 45, TimeSpan.FromHours(7));
            var rows = new[]
            {
                CreateBooking(tripId),
                CreateBooking(tripId),
                CreateBooking(tripId),
                CreateBooking(tripId),
                CreateBooking(tripId),
                CreateBooking(tripId),
                CreateBooking(tripId),
                CreateBooking(otherTripId),
            };
            db.Bookings.AddRange(rows);
            await db.SaveChangesAsync();
            await SetStatusAsync(db, rows[0].Id, BookingStatus.CONFIRMED);
            await SetStatusAsync(db, rows[1].Id, BookingStatus.PARTIAL_NO_SHOW);
            await SetStatusAsync(db, rows[2].Id, BookingStatus.NO_SHOW);
            await SetStatusAsync(db, rows[3].Id, BookingStatus.CANCELLED);
            await SetStatusAsync(db, rows[4].Id, BookingStatus.REFUNDED);
            await SetStatusAsync(db, rows[5].Id, BookingStatus.COMPLETED);
            await SetStatusAsync(db, rows[6].Id, BookingStatus.PENDING_PAYMENT);
            await SetStatusAsync(db, rows[7].Id, BookingStatus.CONFIRMED);
            db.ChangeTracker.Clear();

            var handler = CreateHandler(db);
            var unitOfWork = new EfUnitOfWork(db);
            var command = new HandleTripCompletedCommand(tripId, completedAt, HasSubstitution: true);
            var firstChanged = await unitOfWork.ExecuteInTransactionAsync(
                () => handler.Handle(command, CancellationToken.None),
                CancellationToken.None);
            var afterFirst = await ReadStateAsync(db, rows);
            var firstHistory = await db.BookingStatusHistories.AsNoTracking()
                .Where(row => rows.Select(booking => booking.Id).Contains(row.BookingId))
                .ToListAsync();

            var duplicateChanged = await unitOfWork.ExecuteInTransactionAsync(
                () => handler.Handle(command, CancellationToken.None),
                CancellationToken.None);
            var afterDuplicate = await ReadStateAsync(db, rows);
            var duplicateHistory = await db.BookingStatusHistories.AsNoTracking()
                .Where(row => rows.Select(booking => booking.Id).Contains(row.BookingId))
                .ToListAsync();

            firstChanged.Should().Be(2);
            duplicateChanged.Should().Be(0);
            afterFirst[rows[0].Id].Status.Should().Be(BookingStatus.COMPLETED);
            afterFirst[rows[1].Id].Status.Should().Be(BookingStatus.COMPLETED);
            afterFirst[rows[0].Id].CompletedAt.Should().Be(completedAt);
            afterFirst[rows[1].Id].CompletedAt.Should().Be(completedAt);
            afterFirst[rows[2].Id].Status.Should().Be(BookingStatus.NO_SHOW);
            afterFirst[rows[3].Id].Status.Should().Be(BookingStatus.CANCELLED);
            afterFirst[rows[4].Id].Status.Should().Be(BookingStatus.REFUNDED);
            afterFirst[rows[5].Id].Status.Should().Be(BookingStatus.COMPLETED);
            afterFirst[rows[6].Id].Status.Should().Be(BookingStatus.PENDING_PAYMENT);
            afterFirst[rows[7].Id].Status.Should().Be(BookingStatus.CONFIRMED);
            afterFirst.Where(pair => pair.Key != rows[0].Id && pair.Key != rows[1].Id)
                .Should().OnlyContain(pair => pair.Value.CompletedAt == null);
            firstHistory.Should().HaveCount(2);
            firstHistory.Should().OnlyContain(row =>
                row.Status == BookingStatus.COMPLETED
                && row.OccurredAt == completedAt
                && row.Source == "COMPLETE_ON_TRIP_COMPLETED"
                && row.ActorUserId == null
                && row.ReasonCode == null);
            firstHistory.Select(row => row.BookingId).Should()
                .BeEquivalentTo([rows[0].Id, rows[1].Id]);
            afterDuplicate.Should().BeEquivalentTo(afterFirst);
            duplicateHistory.Should().BeEquivalentTo(firstHistory);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task FailureAfterMutationAndHistoryStaging_RollsBackBoth()
    {
        var databaseName = $"vietride_booking_trip_completed_rollback_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var db = CreateDbContext(dataSource);
            await db.Database.MigrateAsync();
            var tripId = Guid.NewGuid();
            var completedAt = new DateTimeOffset(2026, 7, 14, 19, 0, 0, TimeSpan.FromHours(7));
            var booking = CreateBooking(tripId);
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            await SetStatusAsync(db, booking.Id, BookingStatus.CONFIRMED);
            db.ChangeTracker.Clear();

            var bookingRepository = CreateBookingRepository(db);
            var realHistory = CreateHistoryRepository(db);
            var handler = new HandleTripCompletedCommandHandler(
                bookingRepository,
                new ThrowAfterStagingHistoryRepository(realHistory));
            var unitOfWork = new EfUnitOfWork(db);

            var act = () => unitOfWork.ExecuteInTransactionAsync(
                () => handler.Handle(
                    new HandleTripCompletedCommand(tripId, completedAt, HasSubstitution: false),
                    CancellationToken.None),
                CancellationToken.None);

            await act.Should().ThrowAsync<ForcedFailureException>();
            db.ChangeTracker.Clear();
            var persisted = await db.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id);
            persisted.Status.Should().Be(BookingStatus.CONFIRMED);
            persisted.CompletedAt.Should().BeNull();
            (await db.BookingStatusHistories.AsNoTracking().CountAsync(row => row.BookingId == booking.Id))
                .Should().Be(0);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static HandleTripCompletedCommandHandler CreateHandler(BookingDbContext db)
        => new(CreateBookingRepository(db), CreateHistoryRepository(db));

    private static IBookingRepository CreateBookingRepository(BookingDbContext db)
        => (IBookingRepository)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingRepository",
                throwOnError: true)!,
            db)!;

    private static IBookingStatusHistoryRepository CreateHistoryRepository(BookingDbContext db)
        => (IBookingStatusHistoryRepository)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingStatusHistoryRepository",
                throwOnError: true)!,
            db)!;

    private static BookingEntity CreateBooking(Guid tripId)
        => BookingEntity.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow),
            Guid.NewGuid(),
            tripId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000));

    private static async Task SetStatusAsync(
        BookingDbContext db,
        Guid bookingId,
        BookingStatus status)
        => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_booking.bookings
            SET status = CAST({status.ToString()} AS public.booking_status),
                completed_at = NULL
            WHERE id = {bookingId};
            """);

    private static async Task<Dictionary<Guid, BookingState>> ReadStateAsync(
        BookingDbContext db,
        IReadOnlyCollection<BookingEntity> rows)
    {
        var ids = rows.Select(row => row.Id).ToArray();
        return await db.Bookings.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(
                row => row.Id,
                row => new BookingState(row.Status, row.CompletedAt, row.UpdatedAt));
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        BookingDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static BookingDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new BookingDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_BOOKING_TEST_CONNECTION_STRING");
        var template = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        var connectionString = template.Replace(
            "{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase);
        return new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record BookingState(
        BookingStatus Status,
        DateTimeOffset? CompletedAt,
        DateTimeOffset UpdatedAt);

    private sealed class ThrowAfterStagingHistoryRepository(
        IBookingStatusHistoryRepository inner) : IBookingStatusHistoryRepository
    {
        public async Task AddAsync(BookingStatusHistory history, CancellationToken ct = default)
        {
            await inner.AddAsync(history, ct);
            throw new ForcedFailureException();
        }

        public IQueryable<BookingStatusHistory> QueryNoTracking() => inner.QueryNoTracking();
    }

    private sealed class ForcedFailureException : Exception;
}
