using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Persistence.Repositories;

namespace VietRide.Trip.IntegrationTests.Services;

public sealed class TripVehicleSwapServiceIntegrationTests
{
    [Theory]
    [InlineData(true, TripAuditAction.TripVehicleSwapped)]
    [InlineData(true, TripAuditAction.DriverScheduleCascadeApplied)]
    [InlineData(false, TripAuditAction.TripVehicleSwapped)]
    public async Task CallerOwnedTransaction_CommitsOrRollsBackTripSeatsAuditAndOutboxAtomically(
        bool commit,
        string auditAction)
    {
        var databaseName = $"vietride_trip_vehicle_swap_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db);
            var trips = CreateInternal<ITripRepository>(
                "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
                db);
            var seats = CreateInternal<ITripSeatRepository>(
                "VietRide.Trip.Infrastructure.Persistence.Repositories.TripSeatRepository",
                db);
            var vehicles = CreateInternal<IVehicleRepository>(
                "VietRide.Trip.Infrastructure.Persistence.Repositories.VehicleRepository",
                db);
            var audits = new TripAuditLogRepository(db);
            var outbox = new IntegrationEventOutbox(new OutboxStore(db, new SystemClock()));
            var service = new TripVehicleSwapService(seats, audits, outbox);

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var trip = await trips.AcquireForVehicleSwapAsync(seed.TripId, CancellationToken.None);
                var lockedSeats = await seats.AcquireForVehicleSwapAsync(seed.TripId, CancellationToken.None);
                var lockedVehicles = await vehicles.AcquireForVehicleSwapAsync(
                    seed.OperatorId,
                    [seed.OldVehicleId, seed.NewVehicleId],
                    CancellationToken.None);

                await service.StageSwapAsync(
                    trip!,
                    lockedVehicles.Single(vehicle => vehicle.Id == seed.OldVehicleId),
                    lockedVehicles.Single(vehicle => vehicle.Id == seed.NewVehicleId),
                    lockedSeats,
                    [new VehicleSwapBookingSeatImpact(
                        seed.BookingId,
                        ["A01"],
                        VehicleSwapBookingSeatImpact.SeatRemoved)],
                    seed.ActorUserId,
                    auditAction,
                    "  request-1  ",
                    new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero),
                    CancellationToken.None);
                await db.SaveChangesAsync();

                if (commit)
                {
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }
            }

            await using var assertionDb = CreateDbContext(databaseName);
            var persistedTrip = await assertionDb.Trips.SingleAsync(item => item.Id == seed.TripId);
            var persistedSeats = await assertionDb.TripSeats
                .Where(item => item.TripId == seed.TripId)
                .OrderBy(item => item.SeatNumber)
                .ToArrayAsync();
            var auditRows = await assertionDb.TripAuditLogs
                .Where(item => item.TripId == seed.TripId)
                .ToArrayAsync();
            var outboxRows = await assertionDb.OutboxEvents.ToArrayAsync();

            if (commit)
            {
                persistedTrip.VehicleId.Should().Be(seed.NewVehicleId);
                persistedSeats.Select(item => item.SeatNumber).Should().Equal("A01", "A03");
                persistedSeats.Single(item => item.SeatNumber == "A01").Status.Should().Be(TripSeatStatus.BOOKED);
                auditRows.Should().ContainSingle();
                auditRows[0].Action.Should().Be(auditAction);
                auditRows[0].Metadata.Should().NotBeNull();
                var metadata = auditRows[0].Metadata!.Value;
                metadata.EnumerateObject().Select(property => property.Name)
                    .Should().Equal("changedFields", "before", "after", "requestId");
                metadata.GetProperty("changedFields").EnumerateArray().Select(item => item.GetString())
                    .Should().Equal("vehicleId");
                var before = metadata.GetProperty("before");
                before.EnumerateObject().Select(property => property.Name).Should().Equal("vehicleId");
                before.GetProperty("vehicleId").GetGuid().Should().Be(seed.OldVehicleId);
                var after = metadata.GetProperty("after");
                after.EnumerateObject().Select(property => property.Name).Should().Equal("vehicleId");
                after.GetProperty("vehicleId").GetGuid().Should().Be(seed.NewVehicleId);
                metadata.GetProperty("requestId").GetString().Should().Be("request-1");
                outboxRows.Should().ContainSingle(item => item.EventType == "trip.trip.vehicle_swapped");
                outboxRows[0].Payload.Should().Contain("\"assistantUserId\":null");
            }
            else
            {
                persistedTrip.VehicleId.Should().Be(seed.OldVehicleId);
                persistedSeats.Select(item => item.SeatNumber).Should().Equal("A01", "A02");
                auditRows.Should().BeEmpty();
                outboxRows.Should().BeEmpty();
            }
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task LockPrimitives_RejectUseOutsideCallerOwnedTransactionBeforeQuerying()
    {
        await using var db = CreateDbContext("unused");
        var trips = CreateInternal<ITripRepository>(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
            db);
        var seats = CreateInternal<ITripSeatRepository>(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripSeatRepository",
            db);
        var vehicles = CreateInternal<IVehicleRepository>(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.VehicleRepository",
            db);

        await FluentActions.Invoking(() => trips.AcquireForVehicleSwapAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*caller-owned transaction*");
        await FluentActions.Invoking(() => seats.AcquireForVehicleSwapAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*caller-owned transaction*");
        await FluentActions.Invoking(() => vehicles.AcquireForVehicleSwapAsync(
                Guid.NewGuid(),
                [Guid.NewGuid()],
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*caller-owned transaction*");
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var connectionString = $"Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;
        return new TripDbContext(options, new SystemClock());
    }

    private static async Task<Seed> SeedAsync(TripDbContext db)
    {
        var operatorId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var vehicleTypeId = Guid.NewGuid();
        var oldVehicleId = Guid.NewGuid();
        var newVehicleId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var departure = new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero);
        const string oldLayout = """
            {"version":1,"vehicleTypeCode":"TEST","totalSeats":2,"rows":1,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"type":"VIP","isWindow":false,"isAisle":false,"disabled":false},{"seatNumber":"A02","row":1,"col":2,"deck":1,"type":"STANDARD","isWindow":false,"isAisle":false,"disabled":false}]}
            """;
        const string newLayout = """
            {"version":1,"vehicleTypeCode":"TEST","totalSeats":1,"rows":1,"cols":1,"decks":1,"aisles":[],"seats":[{"seatNumber":"A03","row":1,"col":1,"deck":1,"type":"STANDARD","isWindow":false,"isAisle":false,"disabled":false}]}
            """;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_trip.stations (id, name, slug, city, province)
            VALUES
                ({originId}, 'Swap Origin', {$"swap-origin-{originId:N}"}, 'HCMC', 'HCMC'),
                ({destinationId}, 'Swap Destination', {$"swap-destination-{destinationId:N}"}, 'Da Nang', 'Da Nang');
            INSERT INTO vietride_trip.routes
                (id, operator_id, name, origin_station_id, destination_station_id, base_fare, estimated_duration_minutes)
            VALUES
                ({routeId}, {operatorId}, 'Swap Route', {originId}, {destinationId}, 100000, 240);
            INSERT INTO vietride_trip.vehicle_types (id, code, display_name, default_seat_count)
            VALUES ({vehicleTypeId}, {$"SWAP_{vehicleTypeId:N}"}, 'Swap vehicle', 2);
            INSERT INTO vietride_trip.vehicles
                (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats)
            VALUES
                ({oldVehicleId}, {operatorId}, {vehicleTypeId}, {$"OLD-{oldVehicleId:N}"[..20]}, CAST({oldLayout} AS jsonb), 2),
                ({newVehicleId}, {operatorId}, {vehicleTypeId}, {$"NEW-{newVehicleId:N}"[..20]}, CAST({newLayout} AS jsonb), 1);
            INSERT INTO vietride_trip.trips
                (id, operator_id, route_id, vehicle_id, driver_user_id, departure_date_time,
                 estimated_arrival_time, source, base_fare)
            VALUES
                ({tripId}, {operatorId}, {routeId}, {oldVehicleId}, {driverUserId}, {departure},
                 {departure.AddHours(4)}, 'MANUAL', 100000);
            INSERT INTO vietride_trip.trip_seats (id, trip_id, seat_number, seat_type, status)
            VALUES
                ({Guid.NewGuid()}, {tripId}, 'A01', 'VIP', 'BOOKED'),
                ({Guid.NewGuid()}, {tripId}, 'A02', 'STANDARD', 'AVAILABLE');
            """);
        db.ChangeTracker.Clear();

        return new Seed(operatorId, actorUserId, bookingId, tripId, oldVehicleId, newVehicleId);
    }

    private static T CreateInternal<T>(string typeName, TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        return (T)Activator.CreateInstance(type, db)!;
    }

    private sealed record Seed(
        Guid OperatorId,
        Guid ActorUserId,
        Guid BookingId,
        Guid TripId,
        Guid OldVehicleId,
        Guid NewVehicleId);
}
