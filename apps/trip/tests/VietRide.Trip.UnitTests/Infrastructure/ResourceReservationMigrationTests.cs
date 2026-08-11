using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Infrastructure.Migrations;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class ResourceReservationMigrationTests
{
    [Fact]
    public void UpBackfillsBeforeAddingOverlapConstraintAndCreatesUpdatedAtTrigger()
    {
        var operations = BuildOperations(new AddResourceReservations(), "Up");
        var create = operations.OfType<CreateTableOperation>()
            .Should().ContainSingle(operation => operation.Name == "resource_reservations").Subject;

        create.Schema.Should().Be("vietride_trip");
        create.CheckConstraints.Should().Contain(constraint =>
            constraint.Name == "chk_resource_reservations_source"
            && constraint.Sql.Contains("num_nonnulls", StringComparison.Ordinal));
        var sql = operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();
        var mainBackfill = Array.FindIndex(sql, item => item.Contains("FROM vietride_trip.trips", StringComparison.Ordinal));
        var shuttleBackfill = Array.FindIndex(sql, item => item.Contains("FROM vietride_trip.shuttle_trips", StringComparison.Ordinal));
        var exclusion = Array.FindIndex(sql, item => item.Contains("EXCLUDE USING gist", StringComparison.Ordinal));

        mainBackfill.Should().BeGreaterThanOrEqualTo(0);
        shuttleBackfill.Should().BeGreaterThan(mainBackfill);
        exclusion.Should().BeGreaterThan(shuttleBackfill);
        sql.Should().Contain(item => item.Contains("trg_resource_reservations_updated_at", StringComparison.Ordinal));
        sql.Should().Contain(item => item.Contains(
            "CREATE FUNCTION vietride_trip.trg_set_resource_reservation_updated_at()",
            StringComparison.Ordinal));
        sql.Should().NotContain(item => item.Contains(
            "vietride_trip.trg_set_updated_at()",
            StringComparison.Ordinal));
    }

    [Fact]
    public void DownDropsResourceReservationTriggerFunctionBeforeTable()
    {
        var operations = BuildOperations(new AddResourceReservations(), "Down");
        var cleanupIndex = operations.FindIndex(operation =>
            operation is SqlOperation sql
            && sql.Sql.Contains("DROP FUNCTION IF EXISTS vietride_trip.trg_set_resource_reservation_updated_at()", StringComparison.Ordinal));
        var dropTableIndex = operations.FindIndex(operation =>
            operation is DropTableOperation drop
            && drop.Name == "resource_reservations");

        cleanupIndex.Should().BeGreaterThanOrEqualTo(0);
        dropTableIndex.Should().BeGreaterThan(cleanupIndex);
    }

    [Fact]
    public void DownRemovesBlockedAlertRowsBeforeNarrowingColumn()
    {
        var operations = BuildOperations(new AddAssignmentStartBlockedAlert(), "Down");
        var deleteIndex = operations.FindIndex(operation =>
            operation is SqlOperation sql
            && sql.Sql.Contains("ASSIGNMENT_START_BLOCKED", StringComparison.Ordinal));
        var alterIndex = operations.FindIndex(operation =>
            operation is AlterColumnOperation alter
            && alter.Name == "alert_type"
            && alter.MaxLength == 20);

        deleteIndex.Should().BeGreaterThanOrEqualTo(0);
        alterIndex.Should().BeGreaterThan(deleteIndex);
    }

    private static List<MigrationOperation> BuildOperations(Migration migration, string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
