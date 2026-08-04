using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Infrastructure.Migrations;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class TripShuttleDistanceMigrationTests
{
    [Fact]
    public void UpAddsNonNegativeRoadDistanceConstraint()
    {
        var operations = BuildOperations("Up");

        operations.OfType<AddCheckConstraintOperation>().Should().ContainSingle(operation =>
            operation.Name == "chk_shuttle_passengers_road_distance"
            && operation.Sql == "road_distance_meters IS NULL OR road_distance_meters >= 0");
    }

    [Fact]
    public void DownRefusesRollbackWhenTwoWayRowsWouldBeCollapsed()
    {
        var operations = BuildOperations("Down");

        operations.OfType<SqlOperation>().Should().ContainSingle(operation =>
            operation.Sql.Contains("GROUP BY booking_id, ticket_id", StringComparison.Ordinal)
            && operation.Sql.Contains("two-way manifests exist", StringComparison.Ordinal));
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new ExpandShuttleDistance(), [builder]);
        return builder.Operations;
    }
}
