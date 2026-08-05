using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Booking.Infrastructure.Migrations;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class BookingShuttleIntentMigrationTests
{
    [Fact]
    public void DownRefusesRollbackWhenTwoWayRowsWouldBeCollapsed()
    {
        var operations = BuildOperations("Down");

        operations.OfType<SqlOperation>().Should().ContainSingle(operation =>
            operation.Sql.Contains("GROUP BY booking_id", StringComparison.Ordinal)
            && operation.Sql.Contains("two-way intents exist", StringComparison.Ordinal));
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new ExpandBookingShuttleIntents(), [builder]);
        return builder.Operations;
    }
}
