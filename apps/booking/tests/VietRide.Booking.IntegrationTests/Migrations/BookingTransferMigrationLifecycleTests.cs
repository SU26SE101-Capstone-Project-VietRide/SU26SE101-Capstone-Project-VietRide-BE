using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.IntegrationTests.Migrations;

public sealed class BookingTransferMigrationLifecycleTests
{
    private const string PriorMigration = "20260722093941_AddIntegrationInbox";

    [Fact]
    public async Task DownBackfillsRealNullSeatChainDeterministicallyRestoresSchemaAndDataAndRemovesOnlyDay34Objects()
    {
        await using var dataSource = CreateDataSource();
        await using var db = CreateDbContext(dataSource);
        var migrator = db.GetService<IMigrator>();
        var currentMigration = db.Database.GetMigrations().Last();
        var bookingId = Guid.Parse("34040000-0000-4000-8000-000000000001");
        var passengerWithNewSeat = Guid.Parse("34040000-0000-4000-8000-000000000011");
        var passengerWithOriginalSeat = Guid.Parse("34040000-0000-4000-8000-000000000012");
        var existingSeatPassenger = Guid.Parse("34040000-0000-4000-8000-000000000013");

        await SeedBookingAsync(dataSource, bookingId);
        await SeedPassengerAsync(dataSource, passengerWithNewSeat, bookingId, null);
        await SeedPassengerAsync(dataSource, passengerWithOriginalSeat, bookingId, null);
        await SeedPassengerAsync(dataSource, existingSeatPassenger, bookingId, "E01");
        var latest = new DateTimeOffset(2026, 7, 26, 3, 0, 0, TimeSpan.Zero);
        await SeedTransferAsync(dataSource, Guid.Parse("10000000-0000-4000-8000-000000000001"),
            bookingId, passengerWithNewSeat, Guid.Parse("34040000-0000-4000-8000-000000000101"),
            Guid.Parse("34040000-0000-4000-8000-000000000102"), null, "A01", latest.AddMinutes(-1));
        await SeedTransferAsync(dataSource, Guid.Parse("10000000-0000-4000-8000-000000000002"),
            bookingId, passengerWithNewSeat, Guid.Parse("34040000-0000-4000-8000-000000000102"),
            Guid.Parse("34040000-0000-4000-8000-000000000103"), null, "B02", latest);
        await SeedTransferAsync(dataSource, Guid.Parse("f0000000-0000-4000-8000-000000000003"),
            bookingId, passengerWithNewSeat, Guid.Parse("34040000-0000-4000-8000-000000000103"),
            Guid.Parse("34040000-0000-4000-8000-000000000104"), null, "C03", latest);
        await SeedTransferAsync(dataSource, Guid.Parse("10000000-0000-4000-8000-000000000004"),
            bookingId, passengerWithOriginalSeat, Guid.Parse("34040000-0000-4000-8000-000000000201"),
            Guid.Parse("34040000-0000-4000-8000-000000000202"), "D04", null, latest.AddMinutes(-1));
        await SeedTransferAsync(dataSource, Guid.Parse("f0000000-0000-4000-8000-000000000005"),
            bookingId, passengerWithOriginalSeat, Guid.Parse("34040000-0000-4000-8000-000000000202"),
            Guid.Parse("34040000-0000-4000-8000-000000000203"), "F06", null, latest);

        try
        {
            await migrator.MigrateAsync(PriorMigration);

            (await ReadSeatAsync(dataSource, passengerWithNewSeat)).Should().Be("C03");
            (await ReadSeatAsync(dataSource, passengerWithOriginalSeat)).Should().Be("F06");
            (await ReadSeatAsync(dataSource, existingSeatPassenger)).Should().Be("E01");
            (await IsPassengerSeatNullableAsync(dataSource)).Should().BeFalse();
            (await RelationExistsAsync(dataSource, "booking_transfers")).Should().BeFalse();
            (await EnumExistsAsync(dataSource)).Should().BeFalse();
            (await RelationExistsAsync(dataSource, "bookings")).Should().BeTrue();
            (await RelationExistsAsync(dataSource, "passengers")).Should().BeTrue();
        }
        finally
        {
            await migrator.MigrateAsync(currentMigration);
            await DeleteBookingAsync(dataSource, bookingId);
        }
    }

    [Fact]
    public async Task DownFailsWhenNoRecoverableSeatExistsAndNeverWritesSentinel()
    {
        await using var dataSource = CreateDataSource();
        await using var db = CreateDbContext(dataSource);
        var migrator = db.GetService<IMigrator>();
        var bookingId = Guid.Parse("34040000-0000-4000-8000-000000000002");
        var passengerId = Guid.Parse("34040000-0000-4000-8000-000000000021");

        await SeedBookingAsync(dataSource, bookingId);
        await SeedPassengerAsync(dataSource, passengerId, bookingId, null);
        await SeedTransferAsync(dataSource, Guid.Parse("34040000-0000-4000-8000-000000000031"),
            bookingId, passengerId, Guid.Parse("34040000-0000-4000-8000-000000000301"),
            Guid.Parse("34040000-0000-4000-8000-000000000302"), null, null,
            new DateTimeOffset(2026, 7, 26, 3, 0, 0, TimeSpan.Zero));

        try
        {
            var action = () => migrator.MigrateAsync(PriorMigration);

            await action.Should().ThrowAsync<PostgresException>()
                .WithMessage("*seat_number*NULL*");
            (await ReadSeatAsync(dataSource, passengerId)).Should().BeNull();
            (await RelationExistsAsync(dataSource, "booking_transfers")).Should().BeTrue();
            (await IsPassengerSeatNullableAsync(dataSource)).Should().BeTrue();
        }
        finally
        {
            await DeleteBookingAsync(dataSource, bookingId);
        }
    }

    private static NpgsqlDataSource CreateDataSource()
    {
        var connectionString = Environment.GetEnvironmentVariable("BOOKING_DESIGN_CONNECTION")
            ?? throw new InvalidOperationException("BOOKING_DESIGN_CONNECTION is required.");
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        BookingDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static BookingDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
            .Options;
        return new BookingDbContext(options, new SystemClock());
    }

    private static async Task SeedBookingAsync(NpgsqlDataSource dataSource, Guid bookingId)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO vietride_booking.bookings (
                id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
                base_fare, discount_amount, total_amount, status, refund_override, created_at, updated_at)
            VALUES (
                @id, @code, @user_id, @trip_id, @operator_id, @station_id,
                100000, 0, 100000, 'CONFIRMED'::public.booking_status, FALSE, now(), now());
            """);
        command.Parameters.AddWithValue("id", bookingId);
        command.Parameters.AddWithValue("code", $"VR-20260726-{bookingId.ToString("N")[..8].ToUpperInvariant()}");
        command.Parameters.AddWithValue("user_id", Guid.NewGuid());
        command.Parameters.AddWithValue("trip_id", Guid.NewGuid());
        command.Parameters.AddWithValue("operator_id", Guid.NewGuid());
        command.Parameters.AddWithValue("station_id", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedPassengerAsync(
        NpgsqlDataSource dataSource,
        Guid passengerId,
        Guid bookingId,
        string? seat)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO vietride_booking.passengers (
                id, booking_id, seat_number, boarding_status, created_at, updated_at)
            VALUES (
                @id, @booking_id, @seat, 'BOARDED'::public.passenger_boarding_status,
                now(), now());
            """);
        command.Parameters.AddWithValue("id", passengerId);
        command.Parameters.AddWithValue("booking_id", bookingId);
        command.Parameters.AddWithValue("seat", seat is null ? DBNull.Value : seat);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedTransferAsync(
        NpgsqlDataSource dataSource,
        Guid transferId,
        Guid bookingId,
        Guid passengerId,
        Guid oldTripId,
        Guid newTripId,
        string? originalSeat,
        string? newSeat,
        DateTimeOffset transferredAt)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO vietride_booking.booking_transfers (
                id, booking_id, passenger_id, original_trip_id, new_trip_id,
                original_seat_number, new_seat_number, confirmation_status,
                transferred_at, transferred_by_user_id, created_at)
            VALUES (
                @id, @booking_id, @passenger_id, @old_trip_id, @new_trip_id,
                @original_seat, @new_seat,
                'PENDING_CONFIRM'::vietride_booking.booking_transfer_confirmation_status,
                @transferred_at, @actor_id, @transferred_at);
            """);
        command.Parameters.AddWithValue("id", transferId);
        command.Parameters.AddWithValue("booking_id", bookingId);
        command.Parameters.AddWithValue("passenger_id", passengerId);
        command.Parameters.AddWithValue("old_trip_id", oldTripId);
        command.Parameters.AddWithValue("new_trip_id", newTripId);
        command.Parameters.AddWithValue("original_seat", originalSeat is null ? DBNull.Value : originalSeat);
        command.Parameters.AddWithValue("new_seat", newSeat is null ? DBNull.Value : newSeat);
        command.Parameters.AddWithValue("transferred_at", transferredAt);
        command.Parameters.AddWithValue("actor_id", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadSeatAsync(NpgsqlDataSource dataSource, Guid passengerId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT seat_number FROM vietride_booking.passengers WHERE id = @id;
            """);
        command.Parameters.AddWithValue("id", passengerId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<bool> IsPassengerSeatNullableAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT is_nullable = 'YES'
            FROM information_schema.columns
            WHERE table_schema = 'vietride_booking'
              AND table_name = 'passengers'
              AND column_name = 'seat_number';
            """);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> RelationExistsAsync(NpgsqlDataSource dataSource, string relationName)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT to_regclass('vietride_booking.' || @relation_name) IS NOT NULL;
            """);
        command.Parameters.AddWithValue("relation_name", relationName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> EnumExistsAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_type AS type
                JOIN pg_namespace AS namespace ON namespace.oid = type.typnamespace
                WHERE namespace.nspname = 'vietride_booking'
                  AND type.typname = 'booking_transfer_confirmation_status');
            """);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DeleteBookingAsync(NpgsqlDataSource dataSource, Guid bookingId)
    {
        await using var command = dataSource.CreateCommand("""
            DELETE FROM vietride_booking.booking_transfers WHERE booking_id = @booking_id;
            DELETE FROM vietride_booking.passengers WHERE booking_id = @booking_id;
            DELETE FROM vietride_booking.bookings WHERE id = @booking_id;
            """);
        command.Parameters.AddWithValue("booking_id", bookingId);
        await command.ExecuteNonQueryAsync();
    }
}
