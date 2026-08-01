using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.IntegrationTests.Migrations;

public sealed class Day23BookingCurrentDepartureBackfillIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "vietride_booking_day23_migration";
    private const string PriorMigration = "20260712182713_AddBookingShuttleIntent";
    private const string CurrentMigration = "20260717000000_AddBookingTripCurrentDeparture";

    private readonly string _connectionString = CreateConnectionString();
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await DropDatabaseAsync();
        await CreateDatabaseAsync();

        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        BookingDbContext.ConfigurePostgresTypes(builder);
        _dataSource = builder.Build();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await DropDatabaseAsync();
    }

    [Fact]
    public async Task Migration_BackfillsCurrentDepartureFromTheImmutableSnapshot()
    {
        var bookingId = Guid.Parse("23232323-2323-4323-8323-232323232323");
        var departure = new DateTimeOffset(2026, 7, 23, 1, 30, 0, TimeSpan.Zero);

        await using var db = CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PriorMigration);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_booking.bookings (
                id,
                booking_code,
                passenger_user_id,
                trip_id,
                operator_id,
                pickup_station_id,
                base_fare,
                discount_amount,
                total_amount,
                status,
                refund_override,
                trip_snapshot_departure,
                created_at,
                updated_at)
            VALUES (
                {bookingId},
                {"VR-20260723-DAY23BF1"},
                {Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")},
                {Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")},
                {Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc")},
                {Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd")},
                200000,
                0,
                200000,
                'PENDING_PAYMENT'::public.booking_status,
                FALSE,
                {departure},
                {departure.AddDays(-1)},
                {departure.AddDays(-1)});
            """);

        await migrator.MigrateAsync(CurrentMigration);

        await using var command = _dataSource!.CreateCommand("""
            SELECT trip_snapshot_departure, trip_current_departure
            FROM vietride_booking.bookings
            WHERE id = @booking_id;
            """);
        command.Parameters.AddWithValue("booking_id", bookingId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetFieldValue<DateTimeOffset>(0).Should().Be(departure);
        reader.GetFieldValue<DateTimeOffset>(1).Should().Be(departure);
    }

    private BookingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(_dataSource!, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new BookingDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString()
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_BOOKING_TEST_CONNECTION_STRING");
        var template = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(template.Replace(
            "{databaseName}", DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = DatabaseName,
        }.ConnectionString;
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private string BuildMaintenanceConnectionString()
        => new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
}
