using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Infrastructure.Migrations;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class TripBusinessCodeMigrationTests
{
    [Fact]
    public void Up_AddsNullableColumnsAndPartialUniqueIndexes()
    {
        var operations = BuildOperations("Up");

        operations.OfType<AddColumnOperation>().Should().SatisfyRespectively(
            tripCode => AssertColumn(tripCode, "trips", "trip_code", 30),
            routeCode => AssertColumn(routeCode, "routes", "code", 20));
        operations.OfType<CreateIndexOperation>().Should().SatisfyRespectively(
            tripCode => AssertIndex(tripCode, "uq_trips_trip_code", "trip_code IS NOT NULL"),
            routeCode => AssertIndex(routeCode, "uq_routes_operator_code", "deleted_at IS NULL AND code IS NOT NULL"));
    }

    [Fact]
    public void Down_DropsBothIndexesBeforeBothColumns()
    {
        var operations = BuildOperations("Down");

        operations.Select(operation => operation.GetType()).Should().Equal(
            typeof(DropIndexOperation),
            typeof(DropIndexOperation),
            typeof(DropColumnOperation),
            typeof(DropColumnOperation));
        operations.OfType<DropIndexOperation>().Select(operation => operation.Name).Should().Equal(
            "uq_trips_trip_code",
            "uq_routes_operator_code");
        operations.OfType<DropColumnOperation>().Select(operation => operation.Name).Should().Equal(
            "trip_code",
            "code");
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddTripBusinessCodesReleaseA(), [builder]);
        return builder.Operations;
    }

    private static void AssertColumn(AddColumnOperation operation, string table, string name, int maxLength)
    {
        operation.Schema.Should().Be("vietride_trip");
        operation.Table.Should().Be(table);
        operation.Name.Should().Be(name);
        operation.IsNullable.Should().BeTrue();
        operation.MaxLength.Should().Be(maxLength);
    }

    private static void AssertIndex(CreateIndexOperation operation, string name, string filter)
    {
        operation.Name.Should().Be(name);
        operation.IsUnique.Should().BeTrue();
        operation.Filter.Should().Be(filter);
    }
}
