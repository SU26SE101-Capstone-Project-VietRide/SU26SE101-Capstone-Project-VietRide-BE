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

    [Fact]
    public void Up_CreatesBookingStatsProcessedEventsTableWithCompositeKey()
    {
        var operations = BuildOperations(new AddBookingStatsProcessedEvents(), "Up");

        var table = operations.OfType<CreateTableOperation>()
            .Single(o => o.Name == "booking_stats_processed_events"
                && o.Schema == "vietride_booking");

        table.PrimaryKey!.Columns.Should().Equal("event_type", "booking_id");
        table.Columns.Should().Contain(c => c.Name == "processed_at"
            && c.DefaultValueSql == "now()");
    }

    [Fact]
    public void Down_DropsBookingStatsProcessedEventsTable()
    {
        var operations = BuildOperations(new AddBookingStatsProcessedEvents(), "Down");

        operations.OfType<DropTableOperation>()
            .Should()
            .Contain(o => o.Name == "booking_stats_processed_events"
                && o.Schema == "vietride_booking");
    }

    [Fact]
    public void BookingStatsRepository_DoesNotOpenNestedTransaction()
    {
        var repositoryPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "VietRide.Booking.Infrastructure",
            "Persistence",
            "Repositories",
            "BookingStatsRepository.cs"));
        var source = File.ReadAllText(repositoryPath);

        source.Should().NotContain("BeginTransactionAsync");
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
