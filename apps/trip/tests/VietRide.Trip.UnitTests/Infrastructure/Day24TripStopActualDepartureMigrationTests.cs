using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure.Migrations;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class Day24TripStopActualDepartureMigrationTests
{
    [Fact]
    public void ModelAndUp_AddExactlyOneNullableActualDepartureColumnWithoutSideEffects()
    {
        typeof(TripStop).GetProperty(nameof(TripStop.ActualDepartureTime))!
            .PropertyType.Should().Be(typeof(DateTimeOffset?));

        var operation = BuildOperations("Up").Should().ContainSingle().Subject;
        operation.Should().BeOfType<AddColumnOperation>();

        var column = (AddColumnOperation)operation;
        column.Name.Should().Be("actual_departure_time");
        column.Table.Should().Be("trip_stops");
        column.Schema.Should().Be("vietride_trip");
        column.ColumnType.Should().Be("timestamp with time zone");
        column.IsNullable.Should().BeTrue();
        column.DefaultValue.Should().BeNull();
        column.DefaultValueSql.Should().BeNull();
    }

    [Fact]
    public void Down_DropsOnlyActualDepartureColumn()
    {
        var operation = BuildOperations("Down").Should().ContainSingle().Subject;

        operation.Should().BeOfType<DropColumnOperation>().Which.Should().Match<DropColumnOperation>(column =>
            column.Name == "actual_departure_time"
            && column.Table == "trip_stops"
            && column.Schema == "vietride_trip");
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddTripStopActualDepartureTime(), [builder]);
        return builder.Operations;
    }
}
