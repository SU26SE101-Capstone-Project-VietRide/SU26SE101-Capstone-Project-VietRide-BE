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
using VietRide.Shared.Application.Security;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Incidents.ReportIncident;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class IncidentPersistenceIntegrationTests
{
    private const string PreviousMigration = "20260714092342_AddTripAuditLogs";
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_UpDownAndReapply_CreatesCanonicalIncidentSchema()
    {
        var databaseName = $"vietride_trip_incident_migration_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName, new FrozenClock(Now));

        try
        {
            await db.Database.MigrateAsync();
            (await TableExistsAsync(db, "incidents")).Should().BeTrue();
            (await IncidentCategoryLabelsAsync(db)).Should().Equal(
                "TRAFFIC_JAM",
                "VEHICLE_BREAKDOWN",
                "ACCIDENT",
                "WEATHER",
                "OTHER");

            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            (await TableExistsAsync(db, "incidents")).Should().BeFalse();
            (await IncidentCategoryLabelsAsync(db)).Should().BeEmpty();

            await migrator.MigrateAsync();
            (await TableExistsAsync(db, "incidents")).Should().BeTrue();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ReportIncident_Transaction_PersistsCanonicalRowAndOutbox()
    {
        var databaseName = $"vietride_trip_incident_success_{Guid.NewGuid():N}";
        var clock = new FrozenClock(Now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var trip = await SeedInProgressTripAsync(db);
            var handler = CreateHandler(
                db,
                new IntegrationEventOutbox(new OutboxStore(db, clock)),
                clock);
            var unitOfWork = new EfUnitOfWork(db);
            var photoPrefix = $"incidents%2F{trip.OperatorId:D}%2F{trip.AssistantUserId!.Value:D}%2F";
            var firstPhoto =
                $"https://firebasestorage.googleapis.com/v0/b/vietride-test.firebasestorage.app/o/{photoPrefix}incident-a.jpg?alt=media";
            var secondPhoto =
                $"https://firebasestorage.googleapis.com/v0/b/vietride-test.firebasestorage.app/o/{photoPrefix}incident-b.jpg?alt=media";

            var response = await unitOfWork.ExecuteInTransactionAsync(
                () => handler.Handle(
                    new ReportIncidentCommand(
                        trip.Id,
                        trip.AssistantUserId!.Value,
                        "ACCIDENT",
                        "  Va chạm nhẹ  ",
                        [$" {firstPhoto} ", secondPhoto],
                        10.7731000m,
                        106.7032000m),
                    CancellationToken.None),
                CancellationToken.None);

            db.ChangeTracker.Clear();
            var persisted = await db.Incidents.AsNoTracking().SingleAsync();
            persisted.Id.Should().Be(response.IncidentId);
            persisted.TripId.Should().Be(trip.Id);
            persisted.ReportedByUserId.Should().Be(trip.AssistantUserId!.Value);
            persisted.Category.Should().Be(IncidentCategory.ACCIDENT);
            persisted.Description.Should().Be("Va chạm nhẹ");
            persisted.PhotoUrls.Should().Equal(
                firstPhoto,
                secondPhoto);
            persisted.Latitude.Should().Be(10.7731000m);
            persisted.Longitude.Should().Be(106.7032000m);
            persisted.ReportedAt.Should().Be(Now);

            var outbox = await db.OutboxEvents.AsNoTracking().SingleAsync();
            outbox.EventType.Should().Be("trip.incident.reported");
            using var payload = JsonDocument.Parse(outbox.Payload);
            payload.RootElement.GetProperty("incidentId").GetGuid().Should().Be(persisted.Id);
            payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(trip.OperatorId);
            payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);

            var unchangedTrip = await db.Trips.AsNoTracking().SingleAsync(entity => entity.Id == trip.Id);
            unchangedTrip.Status.Should().Be(TripStatus.IN_PROGRESS);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ReportIncident_WhenOutboxFails_RollsBackIncident()
    {
        var databaseName = $"vietride_trip_incident_rollback_{Guid.NewGuid():N}";
        var clock = new FrozenClock(Now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var trip = await SeedInProgressTripAsync(db);
            var handler = CreateHandler(db, new ThrowingOutbox(), clock);
            var unitOfWork = new EfUnitOfWork(db);

            var action = () => unitOfWork.ExecuteInTransactionAsync(
                () => handler.Handle(
                    new ReportIncidentCommand(
                        trip.Id,
                        trip.DriverUserId,
                        "OTHER",
                        null,
                        null,
                        null,
                        null),
                    CancellationToken.None),
                CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("OUTBOX_WRITE_FAILED");
            db.ChangeTracker.Clear();
            (await db.Incidents.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static ReportIncidentCommandHandler CreateHandler(
        TripDbContext db,
        IIntegrationEventOutbox outbox,
        IClock clock)
        => new(
            CreateTripRepository(db),
            CreateIncidentRepository(db),
            outbox,
            clock,
            new FirebaseStorageImageUrlValidator("vietride-test.firebasestorage.app"));

    private static ITripRepository CreateTripRepository(TripDbContext db)
        => CreateRepository<ITripRepository>(
            db,
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository");

    private static IIncidentRepository CreateIncidentRepository(TripDbContext db)
        => CreateRepository<IIncidentRepository>(
            db,
            "VietRide.Trip.Infrastructure.Persistence.Repositories.IncidentRepository");

    private static TRepository CreateRepository<TRepository>(TripDbContext db, string typeName)
        where TRepository : class
    {
        var type = typeof(TripDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        return (TRepository)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db],
            culture: null)!;
    }

    private static async Task<VietRide.Trip.Domain.Entities.Trip> SeedInProgressTripAsync(
        TripDbContext db)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Incident Origin",
            $"incident-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m);
        var destination = Station.Create(
            "Incident Destination",
            $"incident-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Incident integration route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            240);
        var vehicleType = VehicleType.Create(
            $"INC_{Guid.NewGuid():N}"[..24],
            "Incident integration vehicle",
            5,
            20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"INC-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            20,
            500m,
            10m);
        var trip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Now.AddHours(-2),
            Now.AddHours(2),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 5m,
            seatLayoutSnapshotJson: vehicle.SeatLayoutJson);
        trip.MarkBoarding(Now.AddHours(-2));
        trip.Start(Now.AddHours(-2));

        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        return trip;
    }

    private static TripDbContext CreateDbContext(string databaseName, IClock clock)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        dataSourceBuilder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(dataSourceBuilder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(dataSourceBuilder.Build(), npgsql =>
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

    private static async Task<bool> TableExistsAsync(TripDbContext db, string tableName)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT to_regclass('vietride_trip.{tableName}') IS NOT NULL";
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<string[]> IncidentCategoryLabelsAsync(TripDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT enumlabel
                FROM pg_enum e
                JOIN pg_type t ON t.oid = e.enumtypid
                JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE n.nspname = 'vietride_trip' AND t.typname = 'incident_category'
                ORDER BY enumsortorder
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var labels = new List<string>();
            while (await reader.ReadAsync())
            {
                labels.Add(reader.GetString(0));
            }

            return labels.ToArray();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class ThrowingOutbox : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("OUTBOX_WRITE_FAILED");
    }
}
