using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class TripStopFareSourcePersistenceTests
{
    [Fact]
    public void Model_MapsExactRequiredSourceContract()
    {
        using var dbContext = CreateDbContext("unused");
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(TripStopFare))
            ?? throw new InvalidOperationException("TripStopFare model missing.");
        var source = entity.FindProperty(nameof(TripStopFare.Source))
            ?? throw new InvalidOperationException("TripStopFare.Source mapping missing.");

        source.GetColumnName().Should().Be("source");
        source.GetColumnType().Should().Be("vietride_trip.trip_stop_fare_source");
        source.IsNullable.Should().BeFalse();
        source.FindAnnotation(RelationalAnnotationNames.DefaultValue).Should().BeNull();
        source.GetDefaultValueSql().Should().BeNull();
        Enum.GetNames<TripStopFareSource>().Should().Equal("TEMPLATE_SNAPSHOT", "MANUAL_OVERRIDE");
    }

    [Fact]
    public async Task Source_PersistsManualOverrideExactly()
    {
        var databaseName = $"vietride_trip_fare_source_{Guid.NewGuid():N}";
        await using var dataSource = CreateDataSource(databaseName);
        await using var dbContext = CreateDbContext(dataSource);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (tripId, stopId) = await SeedTripAndStopAsync(dbContext);
            dbContext.TripStopFares.Add(TripStopFare.Create(
                tripId,
                stopId,
                Money.FromRaw(175_000),
                TripStopFareSource.MANUAL_OVERRIDE));
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var persisted = await dbContext.TripStopFares.AsNoTracking().SingleAsync();
            persisted.Source.Should().Be(TripStopFareSource.MANUAL_OVERRIDE);
            persisted.FareFromThisStop.Amount.Should().Be(175_000);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    internal static async Task<(Guid RouteId, Guid StopId)> SeedRouteAndStopAsync(TripDbContext dbContext)
    {
        var operatorId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var stopId = Guid.NewGuid();

        if (await StationColumnExistsAsync(dbContext, "ward"))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_trip.stations (id, name, slug, city, ward)
                VALUES
                    ({originId}, 'Fare source origin', {$"fare-source-origin-{originId:N}"}, 'Da Nang', 'Hai Chau'),
                    ({destinationId}, 'Fare source destination', {$"fare-source-destination-{destinationId:N}"}, 'Hue', 'Phu Hoi');
                """);
        }
        else
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_trip.stations (id, name, slug, city, province)
                VALUES
                    ({originId}, 'Fare source origin', {$"fare-source-origin-{originId:N}"}, 'Hai Chau', 'Da Nang'),
                    ({destinationId}, 'Fare source destination', {$"fare-source-destination-{destinationId:N}"}, 'Hue City', 'Hue');
                """);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_trip.stops (id, operator_id, name, latitude, longitude)
            VALUES ({stopId}, {operatorId}, 'Fare source stop', 16.1000000, 108.2000000);
            INSERT INTO vietride_trip.routes
                (id, operator_id, name, origin_station_id, destination_station_id, base_fare)
            VALUES
                ({routeId}, {operatorId}, 'Fare source route', {originId}, {destinationId}, 200000);
            """);

        return (routeId, stopId);
    }

    internal static async Task<(Guid TripId, Guid StopId)> SeedTripAndStopAsync(TripDbContext dbContext)
    {
        var (routeId, stopId) = await SeedRouteAndStopAsync(dbContext);
        var operatorId = Guid.NewGuid();
        var vehicleTypeId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var departure = DateTimeOffset.UtcNow.AddDays(10);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_trip.vehicle_types (id, code, display_name, default_seat_count)
            VALUES ({vehicleTypeId}, {$"FARE_{vehicleTypeId:N}"}, 'Fare source vehicle', 20);
            INSERT INTO vietride_trip.vehicles
                (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats)
            VALUES
                ({vehicleId}, {operatorId}, {vehicleTypeId}, {$"FS{vehicleId:N}"[..20]}, jsonb_build_object(), 20);
            INSERT INTO vietride_trip.trips
                (id, operator_id, route_id, vehicle_id, driver_user_id, departure_date_time,
                 estimated_arrival_time, source, base_fare)
            VALUES
                ({tripId}, {operatorId}, {routeId}, {vehicleId}, {driverId}, {departure},
                 {departure.AddHours(3)}, 'MANUAL', 200000);
            """);

        return (tripId, stopId);
    }

    private static async Task<bool> StationColumnExistsAsync(TripDbContext dbContext, string columnName)
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'vietride_trip'
                      AND table_name = 'stations'
                      AND column_name = @columnName)
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "columnName";
            parameter.Value = columnName;
            command.Parameters.Add(parameter);
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    internal static NpgsqlDataSource CreateDataSource(string databaseName)
    {
        var builder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        TripDbContext.ConfigurePostgresEnums(builder);
        return builder.Build();
    }

    internal static TripDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;

        return new TripDbContext(options, new SystemClock());
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(CreateConnectionString(databaseName), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;

        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString =
            "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
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
