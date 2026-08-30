using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.DriverTrips.CompleteTrip;
using VietRide.Trip.Application.Features.DriverTrips.StartTrip;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Jobs;
using VietRide.Trip.IntegrationTests.TestDoubles;
using Domain = VietRide.Trip.Domain;

namespace VietRide.Trip.IntegrationTests.Jobs;

public sealed class TripLifecycleJobIntegrationTests
{
    private static readonly DateTimeOffset InitialNow = DateTimeOffset.Parse("2026-07-14T12:00:00+00:00");

    [Fact]
    public async Task AutoBoarding_UsesInclusiveThirtyMinuteBoundaryAndSecondScanIsIdempotent()
    {
        await WithDatabaseAsync(async db =>
        {
            var clock = new MutableClock(InitialNow);
            var boundary = await SeedTripAsync(db, InitialNow.AddMinutes(30), InitialNow.AddHours(4));
            var outside = await SeedTripAsync(db, InitialNow.AddMinutes(31), InitialNow.AddHours(5));
            var job = CreateAutoBoardingJob(db, clock);

            await job.ScanAsync();
            await job.ScanAsync();

            db.ChangeTracker.Clear();
            (await db.Trips.SingleAsync(trip => trip.Id == boundary.Id)).Status.Should().Be(TripStatus.BOARDING);
            (await db.Trips.SingleAsync(trip => trip.Id == outside.Id)).Status.Should().Be(TripStatus.SCHEDULED);
            var message = await db.OutboxEvents.SingleAsync(item => item.EventType == "trip.trip.boarding_started");
            AssertPayload(message.Payload, boundary.Id, "boardingStartedAt", InitialNow);

            clock.Advance(TimeSpan.FromMinutes(1));
            await job.ScanAsync();
            (await db.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.boarding_started")).Should().Be(2);
        });
    }

    [Fact]
    public async Task AutoStart_UsesStrictDeparturePlusThirtyMinuteBoundaryAndCapturesClockOnce()
    {
        await WithDatabaseAsync(async db =>
        {
            var clock = new MutableClock(InitialNow);
            var boundary = await SeedTripAsync(db, InitialNow.AddMinutes(-30), InitialNow.AddHours(3), TripStatus.BOARDING);
            var eligible = await SeedTripAsync(db, InitialNow.AddMinutes(-31), InitialNow.AddHours(3), TripStatus.BOARDING);
            var job = CreateAutoStartJob(db, clock);

            await job.ScanAsync();
            await job.ScanAsync();

            db.ChangeTracker.Clear();
            (await db.Trips.SingleAsync(trip => trip.Id == boundary.Id)).Status.Should().Be(TripStatus.BOARDING);
            var started = await db.Trips.SingleAsync(trip => trip.Id == eligible.Id);
            started.Status.Should().Be(TripStatus.IN_PROGRESS);
            started.ActualDepartureTime.Should().Be(InitialNow);
            var message = await db.OutboxEvents.SingleAsync(item => item.EventType == "trip.trip.started");
            AssertPayload(message.Payload, eligible.Id, "actualDepartureTime", InitialNow);

            clock.Advance(TimeSpan.FromSeconds(1));
            await job.ScanAsync();
            (await db.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.started")).Should().Be(2);
        });
    }

    [Fact]
    public async Task AutoCompletion_UsesStrictEtaPlusThirtyMinuteBoundaryAndWritesNoAudit()
    {
        await WithDatabaseAsync(async db =>
        {
            var clock = new MutableClock(InitialNow);
            var boundary = await SeedTripAsync(db, InitialNow.AddHours(-4), InitialNow.AddMinutes(-30), TripStatus.IN_PROGRESS);
            var eligible = await SeedTripAsync(db, InitialNow.AddHours(-4), InitialNow.AddMinutes(-31), TripStatus.IN_PROGRESS);
            var job = CreateAutoCompletionJob(db, clock);

            await job.ScanAsync();
            await job.ScanAsync();

            db.ChangeTracker.Clear();
            (await db.Trips.SingleAsync(trip => trip.Id == boundary.Id)).Status.Should().Be(TripStatus.IN_PROGRESS);
            var completed = await db.Trips.SingleAsync(trip => trip.Id == eligible.Id);
            completed.Status.Should().Be(TripStatus.COMPLETED);
            completed.CompletedAt.Should().Be(InitialNow);
            completed.CompletedByUserId.Should().BeNull();
            (await db.TripAuditLogs.CountAsync()).Should().Be(0);
            var message = await db.OutboxEvents.SingleAsync(item => item.EventType == "trip.trip.completed");
            AssertPayload(message.Payload, eligible.Id, "completedAt", InitialNow);

            clock.Advance(TimeSpan.FromSeconds(1));
            await job.ScanAsync();
            (await db.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.completed")).Should().Be(2);
        });
    }

    [Fact]
    public async Task AutoBoarding_WhenTimestampIsEligibleButStatusIsNotScheduled_DoesNothing()
    {
        await WithDatabaseAsync(async db =>
        {
            var candidate = await SeedTripAsync(
                db,
                InitialNow.AddMinutes(30),
                InitialNow.AddHours(4),
                TripStatus.BOARDING);
            var originalActualDepartureTime = candidate.ActualDepartureTime;
            var originalCompletedAt = candidate.CompletedAt;

            await CreateAutoBoardingJob(db, new MutableClock(InitialNow)).ScanAsync();

            db.ChangeTracker.Clear();
            var persisted = await db.Trips.SingleAsync(trip => trip.Id == candidate.Id);
            persisted.Status.Should().Be(TripStatus.BOARDING);
            persisted.ActualDepartureTime.Should().Be(originalActualDepartureTime);
            persisted.CompletedAt.Should().Be(originalCompletedAt);
            (await db.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.boarding_started"))
                .Should().Be(0);
        });
    }

    [Fact]
    public async Task AutoStart_WhenTimestampIsEligibleButStatusIsNotBoarding_DoesNothing()
    {
        await WithDatabaseAsync(async db =>
        {
            var candidate = await SeedTripAsync(
                db,
                InitialNow.AddMinutes(-31),
                InitialNow.AddHours(3));
            var originalActualDepartureTime = candidate.ActualDepartureTime;
            var originalCompletedAt = candidate.CompletedAt;

            await CreateAutoStartJob(db, new MutableClock(InitialNow)).ScanAsync();

            db.ChangeTracker.Clear();
            var persisted = await db.Trips.SingleAsync(trip => trip.Id == candidate.Id);
            persisted.Status.Should().Be(TripStatus.SCHEDULED);
            persisted.ActualDepartureTime.Should().Be(originalActualDepartureTime);
            persisted.CompletedAt.Should().Be(originalCompletedAt);
            (await db.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.started"))
                .Should().Be(0);
        });
    }

    [Fact]
    public async Task AutoCompletion_WhenTimestampIsEligibleButStatusIsNotInProgress_DoesNothing()
    {
        await WithDatabaseAsync(async db =>
        {
            var candidate = await SeedTripAsync(
                db,
                InitialNow.AddHours(-4),
                InitialNow.AddMinutes(-31),
                TripStatus.BOARDING);
            var originalActualDepartureTime = candidate.ActualDepartureTime;
            var originalCompletedAt = candidate.CompletedAt;

            await CreateAutoCompletionJob(db, new MutableClock(InitialNow)).ScanAsync();

            db.ChangeTracker.Clear();
            var persisted = await db.Trips.SingleAsync(trip => trip.Id == candidate.Id);
            persisted.Status.Should().Be(TripStatus.BOARDING);
            persisted.ActualDepartureTime.Should().Be(originalActualDepartureTime);
            persisted.CompletedAt.Should().Be(originalCompletedAt);
            (await db.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.completed"))
                .Should().Be(0);
        });
    }

    [Fact]
    public async Task ManualAndFallbackCompletionRace_HasOneWinnerEventAndAuditOnlyForManualWinner()
    {
        var databaseName = $"vietride_trip_job_race_{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName, new MutableClock(InitialNow));
        try
        {
            await setup.Database.MigrateAsync();
            var seeded = await SeedTripAsync(
                setup,
                InitialNow.AddHours(-4),
                InitialNow.AddMinutes(-31),
                TripStatus.IN_PROGRESS);
            setup.ChangeTracker.Clear();

            await using var manualDb = CreateDbContext(databaseName, new MutableClock(InitialNow));
            await using var automaticDb = CreateDbContext(databaseName, new MutableClock(InitialNow));
            var manual = new CompleteTripCommandHandler(
                CreateRepository(manualDb),
                CreateAuditRepository(manualDb),
                CreateOutbox(manualDb, new MutableClock(InitialNow)),
                new DbUnitOfWork(manualDb),
                new MutableClock(InitialNow),
                new ClearParcelImpactClient());
            var automatic = CreateAutoCompletionJob(automaticDb, new MutableClock(InitialNow));

            Exception? manualFailure = null;
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await manual.Handle(
                            new CompleteTripCommand(seeded.Id, seeded.DriverUserId, "DRIVER"),
                            CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        manualFailure = exception;
                    }
                }),
                automatic.ScanAsync());

            if (manualFailure is not null)
            {
                var conflict = manualFailure.Should().BeOfType<CodedConflictException>().Subject;
                conflict.ErrorCode.Should().Be("TRIP_INVALID_TRANSITION");
            }
            await using var assertionDb = CreateDbContext(databaseName, new MutableClock(InitialNow));
            var persisted = await assertionDb.Trips.SingleAsync(trip => trip.Id == seeded.Id);
            persisted.Status.Should().Be(TripStatus.COMPLETED);
            (await assertionDb.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.completed")).Should().Be(1);
            var auditCount = await assertionDb.TripAuditLogs.CountAsync(item => item.TripId == seeded.Id);
            auditCount.Should().Be(manualFailure is null ? 1 : 0);
            persisted.CompletedByUserId.Should().Be(manualFailure is null ? seeded.DriverUserId : null);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ManualAndAutoBoardingRace_EmitsOnce_AllowsImmediateStart_AndNeverRegresses()
    {
        var databaseName = $"vietride_trip_boarding_race_{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName, new MutableClock(InitialNow));
        try
        {
            await setup.Database.MigrateAsync();
            var seeded = await SeedTripAsync(
                setup,
                InitialNow.AddMinutes(20),
                InitialNow.AddHours(4));
            setup.ChangeTracker.Clear();

            await using var manualDb = CreateDbContext(databaseName, new MutableClock(InitialNow));
            await using var automaticDb = CreateDbContext(databaseName, new MutableClock(InitialNow));
            var manual = CreateBoardingCoordinator(manualDb, new MutableClock(InitialNow));
            var automatic = CreateAutoBoardingJob(automaticDb, new MutableClock(InitialNow));

            var manualTask = manual.StartManualAsync(
                seeded.Id,
                seeded.DriverUserId,
                "DRIVER",
                null,
                InitialNow,
                CancellationToken.None);
            await Task.WhenAll(manualTask, automatic.ScanAsync());
            (await manualTask).Status.Should().Be("BOARDING");

            await using (var startDb = CreateDbContext(databaseName, new MutableClock(InitialNow)))
            {
                var start = new StartTripCommandHandler(
                    CreateRepository(startDb),
                    CreateOutbox(startDb, new MutableClock(InitialNow)),
                    new DbUnitOfWork(startDb),
                    new MutableClock(InitialNow));
                var response = await start.Handle(
                    new StartTripCommand(seeded.Id, seeded.DriverUserId),
                    CancellationToken.None);
                response.Status.Should().Be("IN_PROGRESS");
                response.ActualDepartureTime.Should().Be(InitialNow);
            }

            await using (var lateJobDb = CreateDbContext(databaseName, new MutableClock(InitialNow)))
            {
                await CreateAutoBoardingJob(lateJobDb, new MutableClock(InitialNow)).ScanAsync();
            }

            await using var assertionDb = CreateDbContext(databaseName, new MutableClock(InitialNow));
            var persisted = await assertionDb.Trips.SingleAsync(trip => trip.Id == seeded.Id);
            persisted.Status.Should().Be(TripStatus.IN_PROGRESS);
            persisted.ActualDepartureTime.Should().Be(InitialNow);
            (await assertionDb.OutboxEvents.CountAsync(item =>
                item.EventType == "trip.trip.boarding_started")).Should().Be(1);
            (await assertionDb.OutboxEvents.CountAsync(item =>
                item.EventType == "trip.trip.started")).Should().Be(1);
            (await assertionDb.TripAuditLogs.CountAsync(item =>
                item.TripId == seeded.Id
                && item.Action == "TRIP_BOARDING_STARTED_MANUAL")).Should().BeLessThanOrEqualTo(1);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task WithDatabaseAsync(Func<TripDbContext, Task> test)
    {
        var databaseName = $"vietride_trip_jobs_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName, new MutableClock(InitialNow));
        try
        {
            await db.Database.MigrateAsync();
            await test(db);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static AutoBoardingJob CreateAutoBoardingJob(TripDbContext db, IClock clock)
    {
        var repository = CreateRepository(db);
        var coordinator = CreateBoardingCoordinator(db, clock, repository);
        return new AutoBoardingJob(repository, coordinator, clock);
    }

    private static TripBoardingTransitionCoordinator CreateBoardingCoordinator(
        TripDbContext db,
        IClock clock,
        ITripRepository? repository = null) =>
        new(
            repository ?? CreateRepository(db),
            CreateAuditRepository(db),
            CreateOutbox(db, clock),
            new DbUnitOfWork(db),
            new FixedBoardingWindowProvider(TimeSpan.FromMinutes(180)));

    private static AutoStartFallbackJob CreateAutoStartJob(TripDbContext db, IClock clock) =>
        new(db, CreateRepository(db), CreateOutbox(db, clock), clock);

    private static AutoCompletedFallbackJob CreateAutoCompletionJob(TripDbContext db, IClock clock) =>
        new(db, CreateRepository(db), CreateOutbox(db, clock), clock);

    private static IIntegrationEventOutbox CreateOutbox(TripDbContext db, IClock clock) =>
        new IntegrationEventOutbox(new OutboxStore(db, clock));

    private static ITripRepository CreateRepository(TripDbContext db) =>
        (ITripRepository)CreateInternalRepository(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository", db);

    private static ITripAuditLogRepository CreateAuditRepository(TripDbContext db) =>
        (ITripAuditLogRepository)CreateInternalRepository(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripAuditLogRepository", db);

    private static object CreateInternalRepository(string typeName, TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db],
            culture: null)!;
    }

    private static async Task<Domain.Entities.Trip> SeedTripAsync(
        TripDbContext db,
        DateTimeOffset departure,
        DateTimeOffset arrival,
        TripStatus status = TripStatus.SCHEDULED)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Origin",
            $"origin-{Guid.NewGuid():N}",
            "HCM",
            "HCM",
            latitude: 10m,
            longitude: 106m);
        var destination = Station.Create(
            "Destination",
            $"destination-{Guid.NewGuid():N}",
            "DL",
            "LD",
            latitude: 11m,
            longitude: 108m);
        var route = Domain.Entities.Route.Create(
            operatorId, "Lifecycle", origin.Id, destination.Id, Money.FromRaw(100_000), 300m, 240);
        var vehicleType = VehicleType.Create($"JOB_{Guid.NewGuid():N}"[..24], "Job vehicle", 5, 20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(
            operatorId, vehicleType.Id, $"JOB-{Guid.NewGuid():N}"[..20], layout.RootElement, 20, 500m, 10m);
        var trip = Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            departure,
            arrival,
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 5m,
            seatLayoutSnapshotJson: vehicle.SeatLayoutJson);
        if (status is TripStatus.BOARDING or TripStatus.IN_PROGRESS)
        {
            trip.MarkBoarding(departure.AddMinutes(-30));
        }

        if (status == TripStatus.IN_PROGRESS)
        {
            trip.Start(departure);
        }

        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        return trip;
    }

    private static void AssertPayload(string payload, Guid tripId, string timestampName, DateTimeOffset timestamp)
    {
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("tripId").GetGuid().Should().Be(tripId);
        document.RootElement.GetProperty(timestampName).GetDateTimeOffset().Should().Be(timestamp);
    }

    private static TripDbContext CreateDbContext(string databaseName, IClock clock)
    {
        var builder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        builder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(builder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(builder.Build(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TripDbContext(options, clock);
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class FixedBoardingWindowProvider(TimeSpan manualEarlyWindow)
        : ITripBoardingWindowProvider
    {
        public TimeSpan ManualEarlyWindow { get; } = manualEarlyWindow;
    }

    private sealed class DbUnitOfWork : IUnitOfWork
    {
        private readonly TripDbContext db;
        private IDbContextTransaction? transaction;

        public DbUnitOfWork(TripDbContext db) => this.db = db;

        public async Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            db.SaveChangesAsync(cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await transaction!.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
            transaction = null;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (transaction is null)
            {
                return;
            }

            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            transaction = null;
        }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default) => operation();
    }
}
