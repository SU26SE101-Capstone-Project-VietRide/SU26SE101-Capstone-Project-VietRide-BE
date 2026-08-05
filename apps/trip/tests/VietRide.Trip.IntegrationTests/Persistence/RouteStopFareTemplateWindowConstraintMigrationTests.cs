using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class RouteStopFareTemplateWindowConstraintMigrationTests
{
    private const string PreviousMigration = "20260715104549_AddTripEditAuditPersistence";
    private const string CurrentMigration = "20260715114601_AddFareSourceAndWindowGuard";
    private static readonly DateTimeOffset WindowStart = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MigrationScripts_CreateAndRemoveGuardInDependencyOrder()
    {
        using var dbContext = CreateScriptDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        var up = migrator.GenerateScript(PreviousMigration, CurrentMigration);
        up.IndexOf("CREATE EXTENSION IF NOT EXISTS btree_gist", StringComparison.Ordinal)
            .Should().BeLessThan(up.IndexOf("ADD CONSTRAINT ex_route_stop_fare_templates_no_overlap", StringComparison.Ordinal));
        up.Should().Contain("route_id WITH =")
            .And.Contain("stop_id WITH =")
            .And.Contain("tstzrange(effective_from, COALESCE(effective_until, 'infinity'::timestamptz), '[)') WITH &&");

        var down = migrator.GenerateScript(CurrentMigration, PreviousMigration);
        down.IndexOf("DROP CONSTRAINT IF EXISTS ex_route_stop_fare_templates_no_overlap", StringComparison.Ordinal)
            .Should().BeLessThan(down.IndexOf("DROP EXTENSION IF EXISTS btree_gist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migration_BackfillsSourceAndSupportsDownThenReapply()
    {
        var databaseName = $"vietride_trip_fare_guard_migration_{Guid.NewGuid():N}";
        await using var dataSource = TripStopFareSourcePersistenceTests.CreateDataSource(databaseName);
        await using var dbContext = TripStopFareSourcePersistenceTests.CreateDbContext(dataSource);

        try
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var (tripId, stopId) = await TripStopFareSourcePersistenceTests.SeedTripAndStopAsync(dbContext);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_trip.trip_stop_fares (trip_id, stop_id, fare_from_this_stop)
                VALUES ({tripId}, {stopId}, 150000);
                """);

            await migrator.MigrateAsync(CurrentMigration);
            (await ReadScalarAsync<string>(dbContext,
                "SELECT source::text FROM vietride_trip.trip_stop_fares LIMIT 1"))
                .Should().Be("TEMPLATE_SNAPSHOT");
            (await ReadScalarAsync<bool>(dbContext,
                "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')"))
                .Should().BeTrue();
            (await ReadScalarAsync<bool>(dbContext,
                "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_route_stop_fare_templates_no_overlap')"))
                .Should().BeTrue();

            await migrator.MigrateAsync(PreviousMigration);
            (await ReadScalarAsync<bool>(dbContext,
                "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_route_stop_fare_templates_no_overlap')"))
                .Should().BeFalse();
            (await ReadScalarAsync<bool>(dbContext,
                "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')"))
                .Should().BeFalse();

            await migrator.MigrateAsync(CurrentMigration);
            (await ReadScalarAsync<bool>(dbContext,
                "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_route_stop_fare_templates_no_overlap')"))
                .Should().BeTrue();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Constraint_AllowsAdjacencyAndRejectsUpdateAndOpenEndedOverlap()
    {
        var databaseName = $"vietride_trip_fare_guard_behavior_{Guid.NewGuid():N}";
        await using var dataSource = TripStopFareSourcePersistenceTests.CreateDataSource(databaseName);
        await using var dbContext = TripStopFareSourcePersistenceTests.CreateDbContext(dataSource);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (routeId, stopId) = await TripStopFareSourcePersistenceTests.SeedRouteAndStopAsync(dbContext);
            var firstId = Guid.NewGuid();
            var adjacentId = Guid.NewGuid();
            await InsertTemplateAsync(dbContext, firstId, routeId, stopId, WindowStart, WindowStart.AddDays(10));
            await InsertTemplateAsync(dbContext, adjacentId, routeId, stopId, WindowStart.AddDays(10), WindowStart.AddDays(20));

            Func<Task> overlappingUpdate = () => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_trip.route_stop_fare_templates
                SET effective_from = {WindowStart.AddDays(5)}
                WHERE id = {adjacentId};
                """);
            (await overlappingUpdate.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);

            await InsertTemplateAsync(dbContext, Guid.NewGuid(), routeId, stopId, WindowStart.AddDays(20), null);
            Func<Task> openEndedOverlap = () => InsertTemplateAsync(
                dbContext,
                Guid.NewGuid(),
                routeId,
                stopId,
                WindowStart.AddDays(30),
                WindowStart.AddDays(40));
            (await openEndedOverlap.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Constraint_SerializesConcurrentInsertAndUpdateConflicts()
    {
        var databaseName = $"vietride_trip_fare_guard_concurrent_{Guid.NewGuid():N}";
        await using var dataSource = TripStopFareSourcePersistenceTests.CreateDataSource(databaseName);
        await using var dbContext = TripStopFareSourcePersistenceTests.CreateDbContext(dataSource);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (routeId, stopId) = await TripStopFareSourcePersistenceTests.SeedRouteAndStopAsync(dbContext);

            await AssertConcurrentInsertConflictAsync(dataSource, routeId, stopId);

            var leftId = Guid.NewGuid();
            var rightId = Guid.NewGuid();
            await InsertTemplateAsync(dbContext, leftId, routeId, stopId, WindowStart.AddDays(40), WindowStart.AddDays(50));
            await InsertTemplateAsync(dbContext, rightId, routeId, stopId, WindowStart.AddDays(60), WindowStart.AddDays(70));
            await AssertConcurrentUpdateConflictAsync(dataSource, leftId, rightId);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertConcurrentInsertConflictAsync(
        NpgsqlDataSource dataSource,
        Guid routeId,
        Guid stopId)
    {
        await using var firstConnection = await dataSource.OpenConnectionAsync();
        await using var secondConnection = await dataSource.OpenConnectionAsync();
        await using var firstTransaction = await firstConnection.BeginTransactionAsync();
        await using var secondTransaction = await secondConnection.BeginTransactionAsync();
        await using var first = CreateInsertCommand(
            firstConnection,
            firstTransaction,
            Guid.NewGuid(),
            routeId,
            stopId,
            WindowStart,
            WindowStart.AddDays(20));
        await first.ExecuteNonQueryAsync();

        await using var second = CreateInsertCommand(
            secondConnection,
            secondTransaction,
            Guid.NewGuid(),
            routeId,
            stopId,
            WindowStart.AddDays(10),
            WindowStart.AddDays(30));
        var competingInsert = second.ExecuteNonQueryAsync();
        await firstTransaction.CommitAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => competingInsert);
        exception.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        await secondTransaction.RollbackAsync();
    }

    private static async Task AssertConcurrentUpdateConflictAsync(
        NpgsqlDataSource dataSource,
        Guid leftId,
        Guid rightId)
    {
        await using var firstConnection = await dataSource.OpenConnectionAsync();
        await using var secondConnection = await dataSource.OpenConnectionAsync();
        await using var firstTransaction = await firstConnection.BeginTransactionAsync();
        await using var secondTransaction = await secondConnection.BeginTransactionAsync();
        await using var first = new NpgsqlCommand(
            "UPDATE vietride_trip.route_stop_fare_templates SET effective_until = @until WHERE id = @id",
            firstConnection,
            firstTransaction);
        first.Parameters.AddWithValue("until", WindowStart.AddDays(60));
        first.Parameters.AddWithValue("id", leftId);
        await first.ExecuteNonQueryAsync();

        await using var second = new NpgsqlCommand(
            "UPDATE vietride_trip.route_stop_fare_templates SET effective_from = @from WHERE id = @id",
            secondConnection,
            secondTransaction);
        second.Parameters.AddWithValue("from", WindowStart.AddDays(55));
        second.Parameters.AddWithValue("id", rightId);
        var competingUpdate = second.ExecuteNonQueryAsync();
        await firstTransaction.CommitAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => competingUpdate);
        exception.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        await secondTransaction.RollbackAsync();
    }

    private static NpgsqlCommand CreateInsertCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        Guid routeId,
        Guid stopId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset effectiveUntil)
    {
        var command = new NpgsqlCommand(
            """
            INSERT INTO vietride_trip.route_stop_fare_templates
                (id, route_id, stop_id, fare_from_this_stop, effective_from, effective_until)
            VALUES (@id, @routeId, @stopId, 150000, @effectiveFrom, @effectiveUntil)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("routeId", routeId);
        command.Parameters.AddWithValue("stopId", stopId);
        command.Parameters.AddWithValue("effectiveFrom", effectiveFrom);
        command.Parameters.AddWithValue("effectiveUntil", effectiveUntil);
        return command;
    }

    private static Task<int> InsertTemplateAsync(
        TripDbContext dbContext,
        Guid id,
        Guid routeId,
        Guid stopId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil)
        => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_trip.route_stop_fare_templates
                (id, route_id, stop_id, fare_from_this_stop, effective_from, effective_until)
            VALUES ({id}, {routeId}, {stopId}, 150000, {effectiveFrom}, {effectiveUntil});
            """);

    private static async Task<T> ReadScalarAsync<T>(TripDbContext dbContext, string sql)
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static TripDbContext CreateScriptDbContext()
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(
                "Host=127.0.0.1;Port=5432;Database=unused;Username=vietride;Password=vietride_dev",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;
        return new TripDbContext(options, new VietRide.Shared.Kernel.Abstractions.SystemClock());
    }
}
