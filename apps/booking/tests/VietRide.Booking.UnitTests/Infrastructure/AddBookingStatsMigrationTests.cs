using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Booking.Infrastructure.Migrations;

namespace VietRide.Booking.UnitTests.Infrastructure;

public class AddBookingStatsMigrationTests
{
    [Fact]
    public void Up_CreatesBookingStatsTableWithOperatorNameAndExpressionUniqueIndex()
    {
        var operations = BuildOperations(new AddBookingStats(), "Up");

        operations.OfType<CreateTableOperation>()
            .Single(o => o.Name == "booking_stats")
            .Columns
            .Should()
            .Contain(c => c.Name == "operator_name" && c.IsNullable);

        operations.OfType<SqlOperation>()
            .Should()
            .Contain(o => o.Sql.Contains("uq_booking_stats_operator_date_trip", StringComparison.Ordinal)
                && o.Sql.Contains("COALESCE(trip_id", StringComparison.Ordinal));
    }

    [Fact]
    public void Down_DropsBookingStatsTable()
    {
        var operations = BuildOperations(new AddBookingStats(), "Down");

        operations.OfType<DropTableOperation>()
            .Should()
            .Contain(o => o.Name == "booking_stats" && o.Schema == "vietride_booking");
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(Migration migration, string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        return builder.Operations;
    }
}
