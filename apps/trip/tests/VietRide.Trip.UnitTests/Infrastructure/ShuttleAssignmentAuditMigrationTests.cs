using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Infrastructure.Migrations;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class ShuttleAssignmentAuditMigrationTests
{
    [Fact]
    public void UpCreatesAuditTableWithConstraintForeignKeyAndDescendingIndex()
    {
        var operations = BuildOperations(new AddShuttleAssignmentAuditLog(), "Up");
        var create = operations.OfType<CreateTableOperation>()
            .Should().ContainSingle(operation =>
                operation.Schema == "vietride_trip"
                && operation.Name == "shuttle_trip_assignment_audit_logs").Subject;

        create.Columns.Select(column => column.Name).Should().BeEquivalentTo(
            "id",
            "shuttle_trip_id",
            "operator_id",
            "actor_user_id",
            "action",
            "metadata",
            "occurred_at",
            "created_at");
        create.CheckConstraints.Should().ContainSingle(constraint =>
            constraint.Name == "chk_shuttle_trip_assignment_audit_logs_action"
            && constraint.Sql.Contains("INITIAL_ASSIGNED", StringComparison.Ordinal)
            && constraint.Sql.Contains("REASSIGNED", StringComparison.Ordinal));
        create.ForeignKeys.Should().ContainSingle(foreignKey =>
            foreignKey.Name == "fk_shuttle_assignment_audit_shuttle_trip"
            && foreignKey.PrincipalSchema == "vietride_trip"
            && foreignKey.PrincipalTable == "shuttle_trips"
            && foreignKey.OnDelete == ReferentialAction.Restrict);

        var index = operations.OfType<CreateIndexOperation>().Should().ContainSingle(index =>
            index.Name == "idx_shuttle_assignment_audit_operator_trip_occurred"
            && index.Schema == "vietride_trip"
            && index.Table == "shuttle_trip_assignment_audit_logs").Subject;

        index.Columns.Should().Equal("operator_id", "shuttle_trip_id", "occurred_at");
        index.IsDescending.Should().Equal(false, false, true);
    }

    [Fact]
    public void DownDropsAuditTable()
    {
        var operations = BuildOperations(new AddShuttleAssignmentAuditLog(), "Down");

        operations.OfType<DropTableOperation>().Should().ContainSingle(operation =>
            operation.Schema == "vietride_trip"
            && operation.Name == "shuttle_trip_assignment_audit_logs");
    }

    private static List<MigrationOperation> BuildOperations(Migration migration, string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
