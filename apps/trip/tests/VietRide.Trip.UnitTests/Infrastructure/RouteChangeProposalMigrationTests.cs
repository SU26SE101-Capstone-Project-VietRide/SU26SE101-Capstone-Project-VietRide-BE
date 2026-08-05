using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Infrastructure.Migrations;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class RouteChangeProposalMigrationTests
{
    [Fact]
    public void UpCreatesProposalTableAndDownDropsIt()
    {
        var up = BuildOperations("Up");
        var tables = up.OfType<CreateTableOperation>().ToArray();
        var table = tables.Should().ContainSingle(operation => operation.Name == "route_change_proposals").Subject;
        table.Name.Should().Be("route_change_proposals");
        table.Schema.Should().Be("vietride_trip");
        table.Columns.Select(column => column.Name).Should().Contain(
            "source_updated_at",
            "rejection_reason",
            "superseded_by_proposal_id",
            "approved_alternative_route_id",
            "resolution_code");
        table.Columns.Select(column => column.Name).Should().NotContain("snapshot_stops");
        table.ForeignKeys.Should().Contain(foreignKey =>
            foreignKey.PrincipalTable == "trips" && foreignKey.OnDelete == ReferentialAction.Restrict);
        table.ForeignKeys.Should().Contain(foreignKey =>
            foreignKey.PrincipalTable == "alternative_routes" && foreignKey.OnDelete == ReferentialAction.Restrict);
        table.ForeignKeys.Should().Contain(foreignKey =>
            foreignKey.PrincipalTable == "incidents" && foreignKey.OnDelete == ReferentialAction.Restrict);
        table.CheckConstraints.Should().Contain(constraint =>
            constraint.Name == "chk_route_change_proposals_custom_geometry"
            && constraint.Sql.Contains("snapshot_path_polyline IS NOT NULL", StringComparison.Ordinal));
        up.OfType<CreateIndexOperation>().Should().Contain(index =>
            index.Name == "idx_route_change_proposals_operator_status_created"
            && index.Columns.SequenceEqual(new[] { "operator_id", "status", "created_at" })
            && index.IsDescending!.SequenceEqual(new[] { false, false, true }));
        up.OfType<CreateIndexOperation>().Should().Contain(index =>
            index.Name == "idx_route_change_proposals_proposer_created"
            && index.Columns.SequenceEqual(new[] { "proposed_by_user_id", "created_at" })
            && index.IsDescending!.SequenceEqual(new[] { false, true }));
        var stopTable = tables.Should().ContainSingle(operation => operation.Name == "route_change_proposal_stops").Subject;
        stopTable.PrimaryKey!.Columns.Should().Equal("proposal_id", "stop_id");
        stopTable.ForeignKeys.Should().Contain(foreignKey =>
            foreignKey.PrincipalTable == "route_change_proposals" && foreignKey.OnDelete == ReferentialAction.Cascade);
        stopTable.ForeignKeys.Should().Contain(foreignKey =>
            foreignKey.PrincipalTable == "stops" && foreignKey.OnDelete == ReferentialAction.Restrict);
        up.OfType<SqlOperation>().Select(operation => operation.Sql)
            .Should().Contain(sql => sql.Contains("trg_route_change_proposal_stops_updated_at", StringComparison.Ordinal));

        var down = BuildOperations("Down");
        down.OfType<DropTableOperation>().Should().Contain(operation =>
            operation.Name == "route_change_proposals" && operation.Schema == "vietride_trip");
        down.OfType<DropTableOperation>().Should().Contain(operation =>
            operation.Name == "route_change_proposal_stops" && operation.Schema == "vietride_trip");
        down.OfType<SqlOperation>().Select(operation => operation.Sql)
            .Should().Contain(sql => sql.Contains("DROP FUNCTION IF EXISTS vietride_trip.trg_set_route_change_proposal_updated_at", StringComparison.Ordinal));
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddRouteChangeProposals(), [builder]);
        return builder.Operations;
    }
}
