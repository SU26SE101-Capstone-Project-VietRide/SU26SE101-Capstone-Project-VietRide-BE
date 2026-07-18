using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Booking.Infrastructure.Migrations;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class AddBookingTripCurrentDepartureMigrationTests
{
    [Fact]
    public void Up_AddsNullableProjectionBackfillsLegacyRowsAndCreatesDescendingIndex()
    {
        var operations = BuildOperations("Up");

        operations.Should().HaveCount(3);
        operations.OfType<AddColumnOperation>().Should().ContainSingle(column =>
            column.Name == "trip_current_departure"
            && column.Table == "bookings"
            && column.Schema == "vietride_booking"
            && column.ColumnType == "timestamp with time zone"
            && column.IsNullable);
        operations.OfType<SqlOperation>().Should().ContainSingle(operation =>
            operation.Sql.Contains("UPDATE vietride_booking.bookings", StringComparison.Ordinal)
            && operation.Sql.Contains("trip_current_departure = trip_snapshot_departure", StringComparison.Ordinal));

        var index = operations.OfType<CreateIndexOperation>().Should().ContainSingle().Subject;
        index.Name.Should().Be("idx_bookings_trip_current_departure");
        index.Table.Should().Be("bookings");
        index.Schema.Should().Be("vietride_booking");
        index.Columns.Should().Equal("trip_current_departure");
        index.IsDescending.Should().NotBeNull(
            "an empty descending array is EF Core's representation for all indexed columns descending");
        (index.IsDescending!.Length == 0 || index.IsDescending.SequenceEqual([true])).Should().BeTrue();
    }

    [Fact]
    public void Down_DropsOnlyTheProjectionIndexAndColumn()
    {
        var operations = BuildOperations("Down");

        operations.Should().HaveCount(2);
        operations.OfType<DropIndexOperation>().Should().ContainSingle(index =>
            index.Name == "idx_bookings_trip_current_departure"
            && index.Table == "bookings"
            && index.Schema == "vietride_booking");
        operations.OfType<DropColumnOperation>().Should().ContainSingle(column =>
            column.Name == "trip_current_departure"
            && column.Table == "bookings"
            && column.Schema == "vietride_booking");
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddBookingTripCurrentDeparture(), [builder]);
        return builder.Operations;
    }
}
