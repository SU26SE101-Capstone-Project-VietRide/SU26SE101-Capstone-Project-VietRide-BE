using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class LocationHierarchyPersistenceTests
{
    private const string PreviousMigration = "20260809140549_AddDriverScheduleBaseFare";

    [Fact]
    public async Task Migration_UpDownAndReapply_ImportsOfficialHierarchyWithoutDeletingBusinessRows()
    {
        var databaseName = $"vietride_trip_location_hierarchy_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);

        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var legacyHcmId = await ScalarAsync<Guid>(db, "SELECT id FROM vietride_trip.locations WHERE code='HCM'");
            var stationId = Guid.NewGuid();
            var stationSlug = $"legacy-{stationId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_trip.stations (id,name,slug,city,ward,location_id)
                VALUES ({stationId}, 'Legacy station', {stationSlug}, 'Ho Chi Minh City', NULL, {legacyHcmId})
                """);

            await migrator.MigrateAsync();

            (await ScalarAsync<long>(db, "SELECT count(*) FROM vietride_trip.locations WHERE parent_location_id IS NULL"))
                .Should().Be(34);
            (await ScalarAsync<long>(db, "SELECT count(*) FROM vietride_trip.locations WHERE parent_location_id IS NOT NULL"))
                .Should().Be(3321);
            (await ScalarAsync<string>(db, """
                SELECT child.code || '|' || child.name || '|' || child.type || '|' || parent.code
                FROM vietride_trip.locations child
                JOIN vietride_trip.locations parent ON parent.id=child.parent_location_id
                WHERE child.code='26506'
                """))
                .Should().Be("26506|Phường Vũng Tàu|WARD|79");
            (await ScalarAsync<Guid>(db, "SELECT location_id FROM vietride_trip.stations WHERE id='" + stationId + "'"))
                .Should().Be(legacyHcmId);
            (await ScalarAsync<string>(db, "SELECT code FROM vietride_trip.locations WHERE id='" + legacyHcmId + "'"))
                .Should().Be("79");
            var leafStationId = Guid.NewGuid();
            var leafStationSlug = $"leaf-{leafStationId:N}";
            var vungTauId = await ScalarAsync<Guid>(db, "SELECT id FROM vietride_trip.locations WHERE code='26506'");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_trip.stations (id,name,slug,city,ward,location_id)
                VALUES ({leafStationId}, 'Leaf station', {leafStationSlug}, 'Thành phố Hồ Chí Minh', 'Phường Vũng Tàu', {vungTauId})
                """);

            await migrator.MigrateAsync(PreviousMigration);
            (await ScalarAsync<long>(db, "SELECT count(*) FROM vietride_trip.locations WHERE code='26506'"))
                .Should().Be(0);
            (await ScalarAsync<string>(db, "SELECT code FROM vietride_trip.locations WHERE id='" + legacyHcmId + "'"))
                .Should().Be("HCM");
            (await ColumnExistsAsync(db, "locations", "parent_location_id")).Should().BeFalse();
            (await ScalarAsync<long>(db, "SELECT count(*) FROM vietride_trip.stations WHERE id='" + leafStationId + "' AND location_id IS NULL"))
                .Should().Be(1);

            await migrator.MigrateAsync();
            (await ScalarAsync<long>(db, "SELECT count(*) FROM vietride_trip.locations WHERE code='26506'"))
                .Should().Be(1);
            (await ColumnExistsAsync(db, "locations", "parent_location_id")).Should().BeTrue();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        dataSourceBuilder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(dataSourceBuilder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(dataSourceBuilder.Build(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TripDbContext(options, new FrozenClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template)) template = fallback;
        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private static async Task<T> ScalarAsync<T>(TripDbContext db, string commandText)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            return (T)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(TripDbContext db, string table, string column)
        => await ScalarAsync<bool>(db, $"""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema='vietride_trip' AND table_name='{table}' AND column_name='{column}')
            """);

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
    }
}
