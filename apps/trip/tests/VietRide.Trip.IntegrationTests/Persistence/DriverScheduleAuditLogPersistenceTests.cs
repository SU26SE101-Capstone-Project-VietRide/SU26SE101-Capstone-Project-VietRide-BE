using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Persistence.Repositories;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class DriverScheduleAuditLogPersistenceTests
{
    private const string PreviousMigration = "20260714092342_AddTripAuditLogs";

    [Fact]
    public void Model_MatchesAppendOnlyAuditContract()
    {
        using var dbContext = CreateDbContext("unused");
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(DriverScheduleAuditLog))
            ?? throw new InvalidOperationException("DriverScheduleAuditLog model missing.");

        entity.GetTableName().Should().Be("driver_schedule_audit_logs");
        entity.GetSchema().Should().Be(TripDbContext.SchemaName);
        entity.GetProperties().Select(property => property.GetColumnName()).Should().BeEquivalentTo(
            "id",
            "driver_schedule_id",
            "actor_user_id",
            "action",
            "metadata",
            "occurred_at",
            "created_at");

        entity.FindProperty(nameof(DriverScheduleAuditLog.CreatedAt))?.GetDefaultValueSql().Should().Be("now()");
        entity.FindProperty(nameof(DriverScheduleAuditLog.Metadata))?.GetColumnType().Should().Be("jsonb");
        entity.FindProperty(nameof(DriverScheduleAuditLog.Action))?.GetMaxLength().Should().Be(64);

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        foreignKey.Properties.Should().ContainSingle(property => property.Name == nameof(DriverScheduleAuditLog.DriverScheduleId));
        foreignKey.PrincipalEntityType.ClrType.Should().Be(typeof(DriverSchedule));
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var indexes = entity.GetIndexes().ToDictionary(index => index.GetDatabaseName()!);
        indexes.Should().HaveCount(3);
        indexes["idx_driver_schedule_audit_logs_schedule_occurred"].IsDescending.Should().Equal(false, true);
        indexes["idx_driver_schedule_audit_logs_actor_occurred"].IsDescending.Should().Equal(false, true);
        indexes["idx_driver_schedule_audit_logs_actor_occurred"].GetFilter().Should().Be("actor_user_id IS NOT NULL");
        indexes["idx_driver_schedule_audit_logs_action_occurred"].IsDescending.Should().Equal(false, true);
    }

    [Fact]
    public void RepositorySurface_ExposesInsertAndReadOnly()
    {
        typeof(IDriverScheduleAuditLogRepository)
            .GetMethods()
            .Select(method => method.Name)
            .Should()
            .BeEquivalentTo("AddAsync", "ListByDriverScheduleIdAsync");
    }

    [Fact]
    public async Task Migration_UpDownAndReapply_CreatesCanonicalPersistence()
    {
        var databaseName = $"vietride_trip_edit_audit_migration_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);

        try
        {
            await dbContext.Database.MigrateAsync();
            (await TableExistsAsync(dbContext, "driver_schedule_audit_logs")).Should().BeTrue();
            (await ColumnExistsAsync(dbContext, "trips", "notes")).Should().BeTrue();

            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            (await TableExistsAsync(dbContext, "driver_schedule_audit_logs")).Should().BeFalse();
            (await ColumnExistsAsync(dbContext, "trips", "notes")).Should().BeFalse();

            await migrator.MigrateAsync();
            (await TableExistsAsync(dbContext, "driver_schedule_audit_logs")).Should().BeTrue();
            (await ColumnExistsAsync(dbContext, "trips", "notes")).Should().BeTrue();

            var seed = SeedTripAndSchedule(dbContext);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var departure = DateTimeOffset.UtcNow.AddDays(1);
            var trip = VietRide.Trip.Domain.Entities.Trip.Create(
                seed.OperatorId,
                seed.RouteId,
                Guid.NewGuid(),
                seed.DriverUserId,
                null,
                seed.DriverScheduleId,
                departure,
                departure.AddHours(4),
                TripSource.MANUAL,
                Money.FromRaw(100_000),
                500m,
                10m,
                0m,
                false,
                "  Dispatch via Gate 3  ");
            var vehicleTypeId = Guid.NewGuid();
            var vehicleId = trip.VehicleId;
            const string seatLayout = "{\"rows\":[]}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_trip.vehicle_types
                    (id, code, display_name, default_seat_count)
                VALUES
                    ({vehicleTypeId}, {$"AUDIT_{vehicleTypeId:N}"}, 'Audit test vehicle', 20);
                INSERT INTO vietride_trip.vehicles
                    (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats, status)
                VALUES
                    ({vehicleId}, {seed.OperatorId}, {vehicleTypeId}, {$"AUD-{vehicleId:N}"[..20]}, {seatLayout}::jsonb, 20, 'ACTIVE');
                INSERT INTO vietride_trip.trips
                    (id, operator_id, route_id, vehicle_id, driver_user_id, driver_schedule_id,
                     departure_date_time, estimated_arrival_time, status, source, base_fare,
                     estimated_passenger_luggage_kg, notes)
                VALUES
                    ({trip.Id}, {seed.OperatorId}, {seed.RouteId}, {vehicleId}, {seed.DriverUserId},
                     {seed.DriverScheduleId}, {departure}, {departure.AddHours(4)}, 'SCHEDULED', 'MANUAL',
                     100000, 0, {trip.Notes});
                """);
            (await ReadTripNotesAsync(dbContext, trip.Id)).Should().Be("Dispatch via Gate 3");

            trip.UpdateNotes("   ");
            trip.Notes.Should().BeNull();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE vietride_trip.trips SET notes = NULL WHERE id = {trip.Id}");
            (await ReadTripNotesAsync(dbContext, trip.Id)).Should().BeNull();

            var repository = new DriverScheduleAuditLogRepository(dbContext);
            var auditLog = DriverScheduleAuditLog.Create(
                Guid.NewGuid(),
                seed.DriverScheduleId,
                Guid.NewGuid(),
                DriverScheduleAuditAction.DriverScheduleEdited,
                "{\"changedFields\":[\"vehicleId\"],\"requestId\":\"request-1\"}",
                DateTimeOffset.UtcNow);
            await repository.AddAsync(auditLog);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var persistedAudit = await repository.ListByDriverScheduleIdAsync(seed.DriverScheduleId);
            persistedAudit.Should().ContainSingle();
            persistedAudit[0].Id.Should().Be(auditLog.Id);
            persistedAudit[0].ActorUserId.Should().Be(auditLog.ActorUserId);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var connectionString = CreateConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
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

    private static PersistenceSeed SeedTripAndSchedule(TripDbContext dbContext)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Audit Origin",
            $"audit-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m);
        var destination = Station.Create(
            "Audit Destination",
            $"audit-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Audit persistence route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            360);
        using var days = JsonDocument.Parse("[1,3,5]");
        var driverUserId = Guid.NewGuid();
        var schedule = DriverSchedule.Create(
            operatorId,
            route.Id,
            null,
            driverUserId,
            null,
            days.RootElement,
            new TimeOnly(8, 0),
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            null,
            true);

        dbContext.AddRange(origin, destination, route, schedule);
        return new PersistenceSeed(operatorId, route.Id, schedule.Id, driverUserId);
    }

    private static async Task<bool> TableExistsAsync(TripDbContext dbContext, string tableName)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT to_regclass('{TripDbContext.SchemaName}.{tableName}') IS NOT NULL";
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        TripDbContext dbContext,
        string tableName,
        string columnName)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = @table AND column_name = @column)
            """;
        var schema = command.CreateParameter();
        schema.ParameterName = "schema";
        schema.Value = TripDbContext.SchemaName;
        command.Parameters.Add(schema);
        var table = command.CreateParameter();
        table.ParameterName = "table";
        table.Value = tableName;
        command.Parameters.Add(table);
        var column = command.CreateParameter();
        column.ParameterName = "column";
        column.Value = columnName;
        command.Parameters.Add(column);

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<string?> ReadTripNotesAsync(TripDbContext dbContext, Guid tripId)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT notes FROM vietride_trip.trips WHERE id = @tripId";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tripId";
        parameter.Value = tripId;
        command.Parameters.Add(parameter);

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            var result = await command.ExecuteScalarAsync();
            return result is null or DBNull ? null : (string)result;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private sealed record PersistenceSeed(
        Guid OperatorId,
        Guid RouteId,
        Guid DriverScheduleId,
        Guid DriverUserId);
}
