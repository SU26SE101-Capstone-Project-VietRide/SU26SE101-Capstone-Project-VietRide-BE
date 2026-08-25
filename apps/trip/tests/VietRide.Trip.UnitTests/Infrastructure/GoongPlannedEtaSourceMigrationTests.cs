using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Trip.Infrastructure.Migrations;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class GoongPlannedEtaSourceMigrationTests
{
    private const string EnumAnnotation = "Npgsql:Enum:vietride_trip.planned_eta_source";

    [Fact]
    public void Up_ExtendsPlannedEtaSourceWithoutDataRewrite()
    {
        var operations = BuildOperations("Up");

        operations.Should().ContainSingle()
            .Which.Should().BeOfType<AlterDatabaseOperation>();
        var alter = operations.OfType<AlterDatabaseOperation>().Single();
        alter[EnumAnnotation].Should().Be("GOOGLE_ROUTES,GOONG,ROUTE_BASELINE");
        alter.OldDatabase[EnumAnnotation].Should().Be("GOOGLE_ROUTES,ROUTE_BASELINE");
        operations.Should().NotContain(operation => operation is SqlOperation);
    }

    [Fact]
    public void Down_RemapsGoongBeforeRestoringHistoricalEnum()
    {
        var operations = BuildOperations("Down");

        operations.Should().HaveCount(2);
        operations.Should().OnlyContain(operation => operation is SqlOperation);
        var sql = operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();
        var remap = sql[0];
        remap.Contains("UPDATE vietride_trip.trips", StringComparison.Ordinal).Should().BeTrue();
        remap.Contains("planned_eta_source = 'GOONG'", StringComparison.Ordinal).Should().BeTrue();
        remap.Contains("THEN 'ROUTE_BASELINE'", StringComparison.Ordinal).Should().BeTrue();
        remap.Contains("GOOGLE_ROUTES'::", StringComparison.Ordinal).Should().BeFalse();
        sql[1].IndexOf("CREATE TYPE vietride_trip.planned_eta_source", StringComparison.Ordinal)
            .Should().BeGreaterThanOrEqualTo(0);
        sql[1].IndexOf("DROP TYPE vietride_trip.planned_eta_source_old", StringComparison.Ordinal)
            .Should().BeGreaterThan(sql[1].IndexOf("CREATE TYPE", StringComparison.Ordinal));
        sql[1].Should().NotContain("'GOONG'");
    }

    [Fact]
    public void Down_RemapPreservesHistoricalAndBaselineRows()
    {
        var before = new[] { "GOOGLE_ROUTES", "GOONG", "ROUTE_BASELINE" };

        var after = before
            .Select(source => source == "GOONG" ? "ROUTE_BASELINE" : source)
            .ToArray();

        after.Should().Equal("GOOGLE_ROUTES", "ROUTE_BASELINE", "ROUTE_BASELINE");
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddGoongPlannedEtaSource(), [builder]);
        return builder.Operations;
    }
}
