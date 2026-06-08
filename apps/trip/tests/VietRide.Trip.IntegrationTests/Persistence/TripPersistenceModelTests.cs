using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class TripPersistenceModelTests
{
    [Fact]
    public void Model_MapsStationOperatorStationAndStopTables_WithExpectedDeleteColumns()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var station = model.FindEntityType(typeof(Station)) ?? throw new InvalidOperationException("Station model missing.");
        var operatorStation = model.FindEntityType(typeof(OperatorStation)) ?? throw new InvalidOperationException("OperatorStation model missing.");
        var stop = model.FindEntityType(typeof(Stop)) ?? throw new InvalidOperationException("Stop model missing.");

        station.GetTableName().Should().Be("stations");
        var stationDeletedAt = station.FindProperty(nameof(Station.DeletedAt));
        var stationIsActive = station.FindProperty(nameof(Station.IsActive));
        stationDeletedAt.Should().NotBeNull();
        stationIsActive.Should().NotBeNull();
        stationDeletedAt!.GetColumnName().Should().Be("deleted_at");
        stationIsActive!.GetColumnName().Should().Be("is_active");

        operatorStation.GetTableName().Should().Be("operator_stations");
        var operatorStationIsActive = operatorStation.FindProperty(nameof(OperatorStation.IsActive));
        operatorStation.FindProperty("DeletedAt").Should().BeNull("operator_stations has no deleted_at column");
        operatorStationIsActive.Should().NotBeNull();
        operatorStationIsActive!.GetColumnName().Should().Be("is_active");

        stop.GetTableName().Should().Be("stops");
        var stopDeletedAt = stop.FindProperty(nameof(Stop.DeletedAt));
        var stopReplacedByStopId = stop.FindProperty(nameof(Stop.ReplacedByStopId));
        stopDeletedAt.Should().NotBeNull();
        stopReplacedByStopId.Should().NotBeNull();
        stopDeletedAt!.GetColumnName().Should().Be("deleted_at");
        stopReplacedByStopId!.GetColumnName().Should().Be("replaced_by_stop_id");
    }

    [Fact]
    public void Model_MapsCanonicalIndexesAndChecks_WithoutCrossDatabaseForeignKey()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var station = model.FindEntityType(typeof(Station))!;
        var operatorStation = model.FindEntityType(typeof(OperatorStation))!;
        var stop = model.FindEntityType(typeof(Stop))!;

        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "uq_stations_slug" && x.IsUnique);
        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stations_city_province");
        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stations_supports_shuttle");
        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stations_name_trgm");

        operatorStation.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "uq_operator_stations_operator_station" && x.IsUnique);
        operatorStation.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_operator_stations_operator_id");
        operatorStation.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_operator_stations_station_id");
        operatorStation.GetForeignKeys().Should().OnlyContain(x => x.PrincipalEntityType.ClrType == typeof(Station));

        stop.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stops_operator_id");
        stop.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stops_replaced_by");
        stop.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stops_shared_suggestion");
        stop.GetCheckConstraints().Should().Contain(x => x.Name == "chk_stops_no_self_replacement");
    }

    private static TripDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=vietride_trip_model_tests;Username=postgres;Password=postgres")
            .Options;

        return new TripDbContext(options, new SystemClock());
    }
}
