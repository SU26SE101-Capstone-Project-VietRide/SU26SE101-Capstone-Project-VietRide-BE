using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Infrastructure.Migrations;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class TripAlternativeRouteMigrationTests
{
    [Fact]
    public void UpAndDownContainColumnAndIndex()
    {
        typeof(TripEntity).GetProperty(nameof(TripEntity.AlternativeRouteId))!
            .PropertyType.Should().Be(typeof(Guid?));

        var up = BuildOperations("Up");
        up.Should().HaveCount(3);

        up.Should().ContainSingle(operation => operation is AddColumnOperation)
            .Which.Should().BeOfType<AddColumnOperation>().Which.Should().Match<AddColumnOperation>(column =>
                column.Name == "alternative_route_id"
                && column.Table == "trips"
                && column.Schema == "vietride_trip"
                && column.ColumnType == "uuid"
                && column.IsNullable);

        up.Should().ContainSingle(operation => operation is CreateIndexOperation)
            .Which.Should().BeOfType<CreateIndexOperation>().Which.Should().Match<CreateIndexOperation>(index =>
                index.Name == "idx_trips_alternative_route_id"
                && index.Table == "trips"
                && index.Schema == "vietride_trip"
                && index.Columns.SequenceEqual(new[] { "alternative_route_id" })
                && !index.IsUnique);

        up.Should().ContainSingle(operation => operation is AddForeignKeyOperation)
            .Which.Should().BeOfType<AddForeignKeyOperation>().Which.Should().Match<AddForeignKeyOperation>(foreignKey =>
                foreignKey.Table == "trips"
                && foreignKey.Schema == "vietride_trip"
                && foreignKey.Columns.SequenceEqual(new[] { "alternative_route_id" })
                && foreignKey.PrincipalTable == "alternative_routes"
                && foreignKey.PrincipalSchema == "vietride_trip"
                && foreignKey.PrincipalColumns!.SequenceEqual(new[] { "id" })
                && foreignKey.OnDelete == ReferentialAction.NoAction);

        var down = BuildOperations("Down");
        down.Should().HaveCount(3);
        down.Should().ContainSingle(operation => operation is DropForeignKeyOperation);
        down.Should().ContainSingle(operation => operation is DropIndexOperation)
            .Which.Should().BeOfType<DropIndexOperation>().Which.Should().Match<DropIndexOperation>(index =>
                index.Name == "idx_trips_alternative_route_id"
                && index.Table == "trips"
                && index.Schema == "vietride_trip");
        down.Should().ContainSingle(operation => operation is DropColumnOperation)
            .Which.Should().BeOfType<DropColumnOperation>().Which.Should().Match<DropColumnOperation>(column =>
                column.Name == "alternative_route_id"
                && column.Table == "trips"
                && column.Schema == "vietride_trip");
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddTripAlternativeRoute(), [builder]);
        return builder.Operations;
    }
}
