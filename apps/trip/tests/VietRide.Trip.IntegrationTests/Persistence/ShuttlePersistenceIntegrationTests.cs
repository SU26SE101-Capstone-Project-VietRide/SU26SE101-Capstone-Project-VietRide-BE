using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Jobs;
using VietRide.Trip.Infrastructure.Messaging;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class ShuttlePersistenceIntegrationTests
{
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new(StringComparer.Ordinal);

    private const string PreviousMigration = "20260710000000_AddVehicleImageUrls";

    [Fact]
    public async Task Migration_UpDownAndReapply_CreatesCanonicalShuttleTables()
    {
        var databaseName = $"vietride_trip_shuttle_migration_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName, new SystemClock());

        try
        {
            await db.Database.MigrateAsync();
            (await TableExistsAsync(db, "shuttle_trips")).Should().BeTrue();
            (await TableExistsAsync(db, "shuttle_passengers")).Should().BeTrue();
            (await TableExistsAsync(db, "shuttle_dispatch_alerts")).Should().BeTrue();

            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            (await TableExistsAsync(db, "shuttle_trips")).Should().BeFalse();

            await migrator.MigrateAsync();
            (await TableExistsAsync(db, "shuttle_trips")).Should().BeTrue();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConfirmedFanOut_ConcurrentReplay_CreatesOneManifestPerTicket()
    {
        var databaseName = $"vietride_trip_shuttle_fanout_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var setup = CreateDbContext(databaseName, clock);

        try
        {
            await setup.Database.MigrateAsync();
            var seed = await SeedBaseAsync(setup, now.AddHours(4));
            var bookingId = Guid.NewGuid();
            var passengerId = Guid.NewGuid();
            var tickets = Enumerable.Range(0, 3)
                .Select(_ => new BookingShuttleConfirmedIntegrationEvent.ConfirmedTicket(Guid.NewGuid(), passengerId))
                .ToArray();
            var integrationEvent = new BookingShuttleConfirmedIntegrationEvent
            {
                BookingId = bookingId,
                TripId = seed.MainTripId,
                UserId = passengerId,
                Tickets = tickets,
                ShuttlePickup = new BookingShuttleConfirmedIntegrationEvent.ShuttlePickupPayload(
                    "12 Nguyen Hue, District 1",
                    10.7731m,
                    106.7032m),
            };

            await using var firstDb = CreateDbContext(databaseName, clock);
            await using var secondDb = CreateDbContext(databaseName, clock);
            var first = CreateConfirmedHandler(firstDb);
            var second = CreateConfirmedHandler(secondDb);

            await Task.WhenAll(
                first.HandleAsync(integrationEvent, CancellationToken.None),
                second.HandleAsync(integrationEvent, CancellationToken.None));

            await using var assertionDb = CreateDbContext(databaseName, clock);
            var manifests = await assertionDb.ShuttlePassengers.AsNoTracking()
                .Where(x => x.BookingId == bookingId)
                .ToArrayAsync();
            manifests.Should().HaveCount(3);
            manifests.Select(x => x.TicketId).Should().OnlyHaveUniqueItems();
            manifests.Should().OnlyContain(x => x.Status == ShuttlePassenger.PendingAssignmentStatus);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task DispatchAndCutoffRace_Repeatedly_PreservesBookingAtomicityAndOutboxConsistency()
    {
        var databaseName = $"vietride_trip_shuttle_race_{Guid.NewGuid():N}";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(5);
        await using var setup = CreateDbContext(databaseName, new FrozenClock(baseTime));

        try
        {
            await setup.Database.MigrateAsync();
            var seed = await SeedBaseAsync(setup, baseTime.AddHours(500));

            for (var iteration = 0; iteration < 12; iteration++)
            {
                var cutoffAt = baseTime.AddMinutes(iteration * 20);
                var trip = VietRide.Trip.Domain.Entities.Trip.Create(
                    seed.OperatorId,
                    seed.RouteId,
                    seed.MainVehicleId,
                    seed.MainDriverId,
                    null,
                    null,
                    cutoffAt.AddMinutes(30),
                    cutoffAt.AddHours(3),
                    TripSource.MANUAL,
                    Money.FromRaw(100_000),
                    500m,
                    5m);
                setup.Trips.Add(trip);
                var bookingId = Guid.NewGuid();
                var passengerId = Guid.NewGuid();
                for (var ticketIndex = 0; ticketIndex < 3; ticketIndex++)
                {
                    setup.ShuttlePassengers.Add(ShuttlePassenger.Request(
                        trip.Id,
                        bookingId,
                        Guid.NewGuid(),
                        passengerId,
                        "12 Nguyen Hue, District 1",
                        10.7731m,
                        106.7032m));
                }

                await setup.SaveChangesAsync();

                var dispatchClock = new FrozenClock(cutoffAt.AddSeconds(-1));
                var cutoffClock = new FrozenClock(cutoffAt);
                await using var dispatchDb = CreateDbContext(databaseName, dispatchClock);
                await using var cutoffDb = CreateDbContext(databaseName, cutoffClock);
                var dispatch = CreateDispatchService(dispatchDb, dispatchClock, seed.OperatorId);
                var safetyJob = new ShuttleDispatchSafetyJob(
                    cutoffDb,
                    CreateOutbox(cutoffDb, cutoffClock),
                    cutoffClock);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var dispatchTask = Task.Run(async () =>
                {
                    await start.Task;
                    try
                    {
                        await dispatch.CreateAsync(new CreateShuttleTripInput(
                            seed.OperatorId,
                            trip.Id,
                            seed.ShuttleDriverId,
                            seed.ShuttleVehicleId,
                            cutoffAt.AddMinutes(-20),
                            cutoffAt.AddMinutes(-5),
                            [bookingId],
                            null), CancellationToken.None);
                    }
                    catch (Exception exception) when (exception.GetType().Name is
                        "ConflictException" or "CodedValidationException")
                    {
                        // The cutoff transaction is an allowed race winner.
                    }
                });
                var cutoffTask = Task.Run(async () =>
                {
                    await start.Task;
                    await safetyJob.ScanAsync(CancellationToken.None);
                });
                start.SetResult();
                await Task.WhenAll(dispatchTask, cutoffTask);

                await using var assertionDb = CreateDbContext(databaseName, cutoffClock);
                var manifests = await assertionDb.ShuttlePassengers.AsNoTracking()
                    .Where(x => x.BookingId == bookingId)
                    .ToArrayAsync();
                manifests.Should().HaveCount(3);
                manifests.Select(x => x.Status).Distinct().Should().ContainSingle();
                var assigned = manifests[0].Status == ShuttlePassenger.PendingStatus;
                var cancelled = manifests[0].Status == ShuttlePassenger.CancelledStatus;
                (assigned || cancelled).Should().BeTrue();

                var shuttleTrips = await assertionDb.ShuttleTrips.AsNoTracking()
                    .CountAsync(x => x.MainTripId == trip.Id);
                var outboxRows = await assertionDb.OutboxEvents.AsNoTracking()
                    .Select(x => new { x.EventType, x.Payload })
                    .ToArrayAsync();
                var outbox = outboxRows
                    .Where(x => x.Payload.Contains(bookingId.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.EventType)
                    .ToArray();
                if (assigned)
                {
                    manifests.Select(x => x.ShuttleTripId).Distinct().Should().ContainSingle();
                    manifests.Should().OnlyContain(x => x.ShuttleTripId.HasValue);
                    shuttleTrips.Should().Be(1);
                    outbox.Should().ContainSingle(x => x == "trip.shuttle.assigned");
                    outbox.Should().NotContain("trip.shuttle.unfulfilled");
                }
                else
                {
                    manifests.Should().OnlyContain(x => !x.ShuttleTripId.HasValue);
                    shuttleTrips.Should().Be(0);
                    outbox.Should().ContainSingle(x => x == "trip.shuttle.unfulfilled");
                    outbox.Should().NotContain("trip.shuttle.assigned");
                }
            }
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<BaseSeed> SeedBaseAsync(TripDbContext db, DateTimeOffset departure)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Shuttle Origin",
            $"shuttle-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m,
            supportsShuttle: true);
        var destination = Station.Create(
            "Shuttle Destination",
            $"shuttle-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Shuttle integration route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            360);
        var vehicleType = VehicleType.Create("SHUTTLE_TEST", "Shuttle integration vehicle", 5, 20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var mainVehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"MAIN-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            20,
            500m,
            10m);
        var shuttleVehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"SHUT-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            12,
            200m,
            5m);
        var mainDriverId = Guid.NewGuid();
        var shuttleDriverId = Guid.NewGuid();
        var mainTrip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            mainVehicle.Id,
            mainDriverId,
            null,
            null,
            departure,
            departure.AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            5m);

        db.AddRange(origin, destination, route, vehicleType, mainVehicle, shuttleVehicle, mainTrip);
        await db.SaveChangesAsync();
        return new BaseSeed(
            operatorId,
            route.Id,
            mainVehicle.Id,
            mainDriverId,
            shuttleVehicle.Id,
            shuttleDriverId,
            mainTrip.Id);
    }

    private static IIntegrationEventHandler<BookingShuttleConfirmedIntegrationEvent> CreateConfirmedHandler(
        TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Messaging.BookingShuttleConfirmedIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<BookingShuttleConfirmedIntegrationEvent>)Activator.CreateInstance(type, db)!;
    }

    private static IShuttleDispatchService CreateDispatchService(
        TripDbContext db,
        IClock clock,
        Guid operatorId)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Services.ShuttleDispatchService",
            throwOnError: true)!;
        return (IShuttleDispatchService)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db, new StubIdentityClient(operatorId), CreateOutbox(db, clock), clock],
            culture: null)!;
    }

    private static IIntegrationEventOutbox CreateOutbox(TripDbContext db, IClock clock)
        => new IntegrationEventOutbox(new OutboxStore(db, clock));

    private static TripDbContext CreateDbContext(string databaseName, IClock clock)
    {
        var connectionString = CreateConnectionString(databaseName);
        var dataSource = DataSources.GetOrAdd(connectionString, static value =>
        {
            var builder = new NpgsqlDataSourceBuilder(value);
            builder.MapEnum<OutboxEventStatus>(
                $"{TripDbContext.SchemaName}.outbox_event_status",
                new NpgsqlNullNameTranslator());
            TripDbContext.ConfigurePostgresEnums(builder);
            return builder.Build();
        });
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TripDbContext(options, clock);
    }

    private static async Task<bool> TableExistsAsync(TripDbContext db, string tableName)
    {
        var wasClosed = db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed;
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT to_regclass('vietride_trip.{tableName}') IS NOT NULL";
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            if (wasClosed)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private sealed record BaseSeed(
        Guid OperatorId,
        Guid RouteId,
        Guid MainVehicleId,
        Guid MainDriverId,
        Guid ShuttleVehicleId,
        Guid ShuttleDriverId,
        Guid MainTripId);

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class StubIdentityClient : IIdentityInternalClient
    {
        private readonly Guid _operatorId;

        public StubIdentityClient(Guid operatorId)
        {
            _operatorId = operatorId;
        }

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityUserLookupResult.Success(
                userId,
                "Shuttle Driver",
                null,
                "DRIVER",
                _operatorId,
                "ACTIVE") with
            {
                Phone = "0900000000",
            });
    }
}
