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

public sealed class TripDestinationArrivalMigrationTests
{
    private const string PreviousMigration = "20260715122504_AddTripIncidents";

    [Fact]
    public async Task Migration_UpDownReapply_ManagesBothDestinationArrivalColumns()
    {
        var databaseName = $"vietride_trip_destination_arrival_migration_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            var migrator = db.GetService<IMigrator>();

            await migrator.MigrateAsync();
            (await DestinationArrivalColumnsAsync(db)).Should().Equal(
                "destination_arrived_at",
                "destination_arrived_by_user_id");

            await migrator.MigrateAsync(PreviousMigration);
            (await DestinationArrivalColumnsAsync(db)).Should().BeEmpty();

            await migrator.MigrateAsync();
            (await DestinationArrivalColumnsAsync(db)).Should().Equal(
                "destination_arrived_at",
                "destination_arrived_by_user_id");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<string[]> DestinationArrivalColumnsAsync(TripDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'vietride_trip'
                  AND table_name = 'trips'
                  AND column_name IN ('destination_arrived_at', 'destination_arrived_by_user_id')
                ORDER BY column_name
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            return columns.ToArray();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var builder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        builder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(builder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(builder.Build(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }
}
