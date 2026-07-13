using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Booking.Infrastructure.Migrations;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class AddBookingStatusHistoryMigrationTests
{
    [Fact]
    public void Up_CreatesAppendOnlyHistoryShapeAndRestrictForeignKey()
    {
        var operations = BuildOperations("Up");
        var table = operations.OfType<CreateTableOperation>()
            .Single(o => o.Name == "booking_status_history" && o.Schema == "vietride_booking");

        table.Columns.Should().Contain(c => c.Name == "reason_code" && c.MaxLength == 100 && c.IsNullable);
        table.Columns.Should().Contain(c => c.Name == "actor_user_id" && c.IsNullable);
        table.Columns.Should().Contain(c => c.Name == "source" && c.MaxLength == 100 && !c.IsNullable);
        table.ForeignKeys.Should().ContainSingle(fk => fk.PrincipalTable == "bookings"
            && fk.OnDelete == ReferentialAction.Restrict);
        table.ForeignKeys.Should().NotContain(fk => fk.Columns.Contains("actor_user_id"));

        operations.OfType<CreateIndexOperation>().Should().ContainSingle(index =>
            index.Name == "idx_booking_status_history_booking_occurred_id"
            && index.Columns.SequenceEqual(new[] { "booking_id", "occurred_at", "id" }));
    }

    [Fact]
    public void Down_DropsHistoryTable()
        => BuildOperations("Down").OfType<DropTableOperation>().Should().ContainSingle(o =>
            o.Name == "booking_status_history" && o.Schema == "vietride_booking");

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddBookingStatusHistory(), [builder]);
        return builder.Operations;
    }
}
