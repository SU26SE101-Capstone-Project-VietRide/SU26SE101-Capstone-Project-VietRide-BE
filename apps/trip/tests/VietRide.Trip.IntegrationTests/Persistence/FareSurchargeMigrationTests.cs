using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class FareSurchargeMigrationTests
{
    private const string PreviousMigration = "20260723090510_AddTripAlternativeRoute";
    private const string CurrentMigration = "20260802183312_AddOperatorFareSurcharges";

    [Fact]
    public void MigrationScript_CreatesPartialInclusiveOverlapConstraintAndReversibleTables()
    {
        using var dbContext = CreateScriptDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        var up = migrator.GenerateScript(PreviousMigration, CurrentMigration);
        up.Should().Contain("CREATE TABLE vietride_trip.operator_fare_surcharge_settings")
            .And.Contain("CREATE TABLE vietride_trip.operator_fare_surcharge_periods")
            .And.Contain("ADD CONSTRAINT ex_operator_fare_surcharge_periods_no_active_overlap")
            .And.Contain("daterange(start_date, end_date + 1, '[)') WITH &&")
            .And.Contain("WHERE (is_active = TRUE AND deleted_at IS NULL)");

        var down = migrator.GenerateScript(CurrentMigration, PreviousMigration);
        down.Should().Contain("DROP CONSTRAINT IF EXISTS ex_operator_fare_surcharge_periods_no_active_overlap")
            .And.Contain("DROP TABLE vietride_trip.operator_fare_surcharge_periods")
            .And.Contain("DROP TABLE vietride_trip.operator_fare_surcharge_settings");
    }

    [Fact]
    public async Task Constraint_SerializesConcurrentInclusiveOverlapButAllowsInactivePeriod()
    {
        var databaseName = $"vietride_trip_fare_surcharge_{Guid.NewGuid():N}";
        await using var dataSource = TripStopFareSourcePersistenceTests.CreateDataSource(databaseName);
        await using var dbContext = TripStopFareSourcePersistenceTests.CreateDbContext(dataSource);

        try
        {
            await dbContext.Database.MigrateAsync();
            var operatorId = Guid.NewGuid();
            var startDate = new DateOnly(2026, 2, 10);
            var endDate = new DateOnly(2026, 2, 20);

            await using var firstConnection = await dataSource.OpenConnectionAsync();
            await using var secondConnection = await dataSource.OpenConnectionAsync();
            await using var firstTransaction = await firstConnection.BeginTransactionAsync();
            await using var secondTransaction = await secondConnection.BeginTransactionAsync();
            await using var first = CreateInsertCommand(
                firstConnection, firstTransaction, operatorId, startDate, endDate, true);
            await first.ExecuteNonQueryAsync();

            await using var second = CreateInsertCommand(
                secondConnection, secondTransaction, operatorId, endDate, endDate.AddDays(5), true);
            var competingInsert = second.ExecuteNonQueryAsync();
            await firstTransaction.CommitAsync();

            var exception = await Assert.ThrowsAsync<PostgresException>(() => competingInsert);
            exception.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
            await secondTransaction.RollbackAsync();

            await using var inactiveConnection = await dataSource.OpenConnectionAsync();
            await using var inactive = CreateInsertCommand(
                inactiveConnection, null, operatorId, startDate.AddDays(1), endDate.AddDays(1), false);
            (await inactive.ExecuteNonQueryAsync()).Should().Be(1);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static NpgsqlCommand CreateInsertCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid operatorId,
        DateOnly startDate,
        DateOnly endDate,
        bool isActive)
    {
        var command = new NpgsqlCommand(
            """
            INSERT INTO vietride_trip.operator_fare_surcharge_periods
                (id, operator_id, name, start_date, end_date, surcharge_percent, is_active)
            VALUES (@id, @operatorId, 'Holiday', @startDate, @endDate, 25, @isActive)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("operatorId", operatorId);
        command.Parameters.AddWithValue("startDate", startDate);
        command.Parameters.AddWithValue("endDate", endDate);
        command.Parameters.AddWithValue("isActive", isActive);
        return command;
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
