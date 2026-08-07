using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class TripPersistenceModelTests
{
    [Fact]
    public void Model_MapsNormalizedNullableTripNotes_WithCanonicalLength()
    {
        using var dbContext = CreateDbContext();
        var tripEntity = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(VietRide.Trip.Domain.Entities.Trip))
            ?? throw new InvalidOperationException("Trip model missing.");
        var notes = tripEntity.FindProperty(nameof(VietRide.Trip.Domain.Entities.Trip.Notes))
            ?? throw new InvalidOperationException("Trip notes property missing.");
        var seatLayoutSnapshot = tripEntity.FindProperty(
                nameof(VietRide.Trip.Domain.Entities.Trip.SeatLayoutSnapshotJson))
            ?? throw new InvalidOperationException("Trip seat-layout snapshot property missing.");

        notes.GetColumnName().Should().Be("notes");
        notes.GetMaxLength().Should().Be(2000);
        notes.IsNullable.Should().BeTrue();
        seatLayoutSnapshot.GetColumnName().Should().Be("seat_layout_snapshot_json");
        seatLayoutSnapshot.IsNullable.Should().BeFalse();

        var trip = CreateTrip("  Dispatch via Gate 3  ");
        trip.Notes.Should().Be("Dispatch via Gate 3");

        trip.UpdateNotes("   ");
        trip.Notes.Should().BeNull();

        trip.UpdateNotes(new string('x', 2000));
        trip.Notes!.Length.Should().Be(2000);
        var tooLong = () => trip.UpdateNotes(new string('x', 2001));
        tooLong.Should().Throw<ArgumentException>();
    }

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
        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stations_city_ward");
        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stations_location_id");
        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stations_supports_shuttle");
        station.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stations_name_trgm");

        operatorStation.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "uq_operator_stations_operator_station" && x.IsUnique);
        operatorStation.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_operator_stations_operator_id");
        operatorStation.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_operator_stations_station_id");
        operatorStation.GetForeignKeys().Should().OnlyContain(x => x.PrincipalEntityType.ClrType == typeof(Station));

        stop.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stops_operator_id");
        stop.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stops_location_id");
        stop.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stops_replaced_by");
        stop.GetForeignKeys().Should().OnlyContain(x => x.PrincipalEntityType.ClrType == typeof(Stop) || x.PrincipalEntityType.ClrType == typeof(Location));
        stop.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_stops_shared_suggestion");
        stop.GetCheckConstraints().Should().Contain(x => x.Name == "chk_stops_no_self_replacement");
    }

    [Fact]
    public void Model_MapsShuttleTables_WithCanonicalConstraintsAndIndexes()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var shuttleTrip = model.FindEntityType(typeof(ShuttleTrip))
            ?? throw new InvalidOperationException("ShuttleTrip model missing.");
        var passenger = model.FindEntityType(typeof(ShuttlePassenger))
            ?? throw new InvalidOperationException("ShuttlePassenger model missing.");
        var alert = model.FindEntityType(typeof(ShuttleDispatchAlert))
            ?? throw new InvalidOperationException("ShuttleDispatchAlert model missing.");

        shuttleTrip.GetTableName().Should().Be("shuttle_trips");
        shuttleTrip.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_shuttle_trips_driver_schedule");
        shuttleTrip.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "idx_shuttle_trips_vehicle_schedule");
        shuttleTrip.GetCheckConstraints().Should().Contain(x => x.Name == "chk_shuttle_trips_schedule");

        passenger.GetTableName().Should().Be("shuttle_passengers");
        passenger.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "uq_shuttle_passengers_booking_ticket_direction" && x.IsUnique);
        passenger.GetForeignKeys().Should().Contain(x => x.PrincipalEntityType.ClrType == typeof(ShuttleTrip)
            && x.DeleteBehavior == DeleteBehavior.SetNull);
        passenger.GetCheckConstraints().Should().Contain(x => x.Name == "chk_shuttle_passengers_status");

        alert.GetTableName().Should().Be("shuttle_dispatch_alerts");
        alert.GetIndexes().Should().Contain(x => x.GetDatabaseName() == "uq_shuttle_dispatch_alerts_trip_type" && x.IsUnique);
        alert.FindProperty(nameof(ShuttleDispatchAlert.UpdatedAt)).Should().BeNull();
    }

    private static TripDbContext CreateDbContext()
    {
        var connectionString = ResolveConnectionString("vietride_trip_model_tests");
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new TripDbContext(options, new SystemClock());
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip(string? notes)
    {
        var departure = DateTimeOffset.UtcNow.AddDays(1);
        return VietRide.Trip.Domain.Entities.Trip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(4),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            100m,
            null,
            0m,
            false,
            notes,
            JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "MODEL",
                totalSeats = 0,
                rows = 0,
                cols = 0,
                decks = 0,
                aisles = Array.Empty<object>(),
                seats = Array.Empty<object>(),
            }));
    }

    private static string ResolveConnectionString(string databaseName)
    {
        const string defaultConnectionString = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";

        var connectionString = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = defaultConnectionString;
        }

        return connectionString.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : connectionString;
    }
}
