using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.DependencyInjection;

namespace VietRide.Trip.IntegrationTests.Internal.Trips;

public sealed class GetTripSnapshotRelationalTests
{
    [Theory]
    [InlineData(false, 175_000)]
    [InlineData(true, 190_000)]
    public async Task Handle_WithAndWithoutPricingAt_ExecutesPostgresFareRepository(
        bool useExplicitPricingAt,
        long expectedFare)
    {
        var databaseName = $"vietride_trip_snapshot_relational_{Guid.NewGuid():N}";
        await using var dataSource = Persistence.TripStopFareSourcePersistenceTests.CreateDataSource(databaseName);
        var pricingAt = new DateTimeOffset(2026, 7, 15, 3, 0, 0, TimeSpan.Zero);
        Guid tripId;
        Guid stopId;

        await using (var setupContext = Persistence.TripStopFareSourcePersistenceTests.CreateDbContext(dataSource))
        {
            await setupContext.Database.MigrateAsync();
            (tripId, stopId) = await SeedSnapshotAsync(setupContext, pricingAt);
        }

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Trip:BackgroundWorkers:Enabled"] = "false",
                    ["Identity:BaseUrl"] = "http://localhost"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddScoped(_ => Persistence.TripStopFareSourcePersistenceTests.CreateDbContext(dataSource));
            services.AddInfrastructure(configuration, backgroundWorkersEnabled: false);

            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var tripStopFares = scope.ServiceProvider.GetRequiredService<ITripStopFareRepository>();
            var compositeKeyLookup = await tripStopFares.GetByIdAsync((tripId, stopId), CancellationToken.None);
            compositeKeyLookup.Should().NotBeNull();

            var handler = new GetTripSnapshotHandler(
                scope.ServiceProvider.GetRequiredService<ITripRepository>(),
                scope.ServiceProvider.GetRequiredService<IRouteRepository>(),
                scope.ServiceProvider.GetRequiredService<IRouteStopFareTemplateRepository>(),
                scope.ServiceProvider.GetRequiredService<IStationRepository>(),
                scope.ServiceProvider.GetRequiredService<IStopRepository>(),
                scope.ServiceProvider.GetRequiredService<ITripSeatRepository>(),
                scope.ServiceProvider.GetRequiredService<ITripStopRepository>(),
                tripStopFares);

            var result = await handler.Handle(
                new GetTripSnapshotQuery(tripId, useExplicitPricingAt ? pricingAt : null),
                CancellationToken.None);

            result.Stops.Should().ContainSingle();
            result.Stops[0].FareFromThisStop.Should().Be(expectedFare);
        }
        finally
        {
            await using var cleanupContext = Persistence.TripStopFareSourcePersistenceTests.CreateDbContext(dataSource);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<(Guid TripId, Guid StopId)> SeedSnapshotAsync(
        TripDbContext dbContext,
        DateTimeOffset pricingAt)
    {
        var operatorId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var vehicleTypeId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var departure = pricingAt.AddDays(10);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_trip.stations (id, name, slug, city, province)
            VALUES
                ({originId}, 'Snapshot origin', {$"snapshot-origin-{originId:N}"}, 'Da Nang', 'Da Nang'),
                ({destinationId}, 'Snapshot destination', {$"snapshot-destination-{destinationId:N}"}, 'Hue', 'Hue');
            INSERT INTO vietride_trip.stops (id, operator_id, name, latitude, longitude)
            VALUES ({stopId}, {operatorId}, 'Snapshot stop', 16.1000000, 108.2000000);
            INSERT INTO vietride_trip.routes
                (id, operator_id, name, origin_station_id, destination_station_id, base_fare)
            VALUES ({routeId}, {operatorId}, 'Snapshot route', {originId}, {destinationId}, 200000);
            INSERT INTO vietride_trip.route_stop_fare_templates
                (id, route_id, stop_id, fare_from_this_stop, effective_from, effective_until)
            VALUES ({Guid.NewGuid()}, {routeId}, {stopId}, 190000, {pricingAt.AddHours(-1)}, {pricingAt.AddHours(1)});
            INSERT INTO vietride_trip.vehicle_types (id, code, display_name, default_seat_count)
            VALUES ({vehicleTypeId}, {$"SNAP_{vehicleTypeId:N}"}, 'Snapshot vehicle', 1);
            INSERT INTO vietride_trip.vehicles
                (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats)
            VALUES
                ({vehicleId}, {operatorId}, {vehicleTypeId}, {$"SP{vehicleId:N}"[..20]}, jsonb_build_object(), 1);
            INSERT INTO vietride_trip.trips
                (id, operator_id, route_id, vehicle_id, driver_user_id, departure_date_time,
                 estimated_arrival_time, source, base_fare)
            VALUES
                ({tripId}, {operatorId}, {routeId}, {vehicleId}, {driverId}, {departure},
                 {departure.AddHours(3)}, 'MANUAL', 200000);
            INSERT INTO vietride_trip.trip_stops
                (trip_id, stop_id, order_index, estimated_arrival_time, allow_pickup, allow_dropoff)
            VALUES ({tripId}, {stopId}, 1, {departure.AddHours(1)}, true, true);
            INSERT INTO vietride_trip.trip_seats (trip_id, seat_number)
            VALUES ({tripId}, 'A01');
            INSERT INTO vietride_trip.trip_stop_fares
                (trip_id, stop_id, fare_from_this_stop, source)
            VALUES ({tripId}, {stopId}, 175000, 'TEMPLATE_SNAPSHOT');
            """);

        return (tripId, stopId);
    }
}
