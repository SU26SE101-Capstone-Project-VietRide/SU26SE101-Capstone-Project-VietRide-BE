using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class TripMigrationModelTests
{
    [Fact]
    public void RuntimeModel_MatchesLatestMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql("Host=localhost;Database=trip_model_check;Username=unused;Password=unused")
            .Options;
        using var context = new TripDbContext(options, new SystemClock());
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;

        snapshot.Should().NotBeNull();
        var snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot!.Model, designTime: true, validationLogger: null)
            .GetRelationalModel();
        var runtimeModel = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var operations = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel, runtimeModel);

        operations.Should().BeEmpty(
            "migration snapshot drifted: {0}",
            string.Join(", ", operations.Select(Describe)));
    }

    private static string Describe(MigrationOperation operation)
        => operation switch
        {
            CreateTableOperation value => $"CreateTable {value.Schema}.{value.Name}",
            DropTableOperation value => $"DropTable {value.Schema}.{value.Name}",
            CreateIndexOperation value => $"CreateIndex {value.Schema}.{value.Table}.{value.Name}",
            DropIndexOperation value => $"DropIndex {value.Schema}.{value.Table}.{value.Name}",
            AddColumnOperation value => $"AddColumn {value.Schema}.{value.Table}.{value.Name}",
            AlterColumnOperation value => $"AlterColumn {value.Schema}.{value.Table}.{value.Name}",
            DropColumnOperation value => $"DropColumn {value.Schema}.{value.Table}.{value.Name}",
            _ => operation.GetType().Name,
        };
}
