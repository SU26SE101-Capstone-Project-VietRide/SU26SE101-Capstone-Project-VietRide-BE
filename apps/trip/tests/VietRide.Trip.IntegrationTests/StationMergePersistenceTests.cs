using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests;

public sealed class StationMergePersistenceTests
{
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new(StringComparer.Ordinal);
    private const string PreviousMigration = "20260715133857_AddTripDestinationArrival";

    [Fact]
    public async Task Migration_UpDownAndReapply_CreatesStationRedirectSchema()
    {
        var databaseName = $"vietride_trip_station_redirect_migration_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            (await ColumnExistsAsync(db, "stations", "merged_into_station_id")).Should().BeTrue();

            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            (await ColumnExistsAsync(db, "stations", "merged_into_station_id")).Should().BeFalse();

            await migrator.MigrateAsync();
            (await ColumnExistsAsync(db, "stations", "merged_into_station_id")).Should().BeTrue();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task MergePrimitives_RelinkAllReferencesCollapseMappingsAndFlattenRedirects()
    {
        var databaseName = $"vietride_trip_station_merge_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedMergeGraphAsync(db);
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var stations = CreateRepository<IStationRepository>("StationRepository", db);
            var operatorStations = CreateRepository<IOperatorStationRepository>("OperatorStationRepository", db);
            var routes = CreateRepository<IRouteRepository>("RouteRepository", db);
            var alternativeRoutes = CreateRepository<IAlternativeRouteRepository>("AlternativeRouteRepository", db);

            var locked = await stations.GetForMergeAsync(seed.PrimaryId, seed.DuplicateId);
            locked.Should().HaveCount(2);
            var primary = locked.Single(station => station.Id == seed.PrimaryId);
            var duplicate = locked.Single(station => station.Id == seed.DuplicateId);
            (await routes.HasStationMergeConflictAsync(duplicate.Id, primary.Id)).Should().BeFalse();
            primary.MergeProfileFrom(duplicate);
            var mappingCounts = await operatorStations.RelinkForStationMergeAsync(duplicate.Id, primary.Id);
            var routeCounts = await routes.RelinkForStationMergeAsync(duplicate.Id, primary.Id);
            var alternativeCount = await alternativeRoutes.RelinkDestinationForStationMergeAsync(duplicate.Id, primary.Id);
            var shuttleCount = await stations.RelinkShuttleTripsAsync(duplicate.Id, primary.Id);
            var redirectCount = await stations.FlattenMergeRedirectsAsync(duplicate.Id, primary.Id);
            duplicate.MarkMergedInto(primary.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            mappingCounts.Should().Be((1, 1));
            routeCounts.Should().Be((1, 1));
            alternativeCount.Should().Be(1);
            shuttleCount.Should().Be(1);
            redirectCount.Should().Be(1);
            await AssertMergedGraphAsync(db, seed);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task RouteConflictPreflight_AllowsTransactionRollbackWithoutPartialMerge()
    {
        var databaseName = $"vietride_trip_station_conflict_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var primary = Station.Create("Primary", $"primary-{Guid.NewGuid():N}", "City", "Province");
            var duplicate = Station.Create(
                "Duplicate",
                $"duplicate-{Guid.NewGuid():N}",
                "City",
                "Province",
                addressStreet: "Duplicate Address");
            var route = Route.Create(
                Guid.NewGuid(),
                "Conflict",
                primary.Id,
                duplicate.Id,
                Money.FromRaw(100_000),
                null,
                null);
            db.AddRange(primary, duplicate, route);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var stations = CreateRepository<IStationRepository>("StationRepository", db);
                var routes = CreateRepository<IRouteRepository>("RouteRepository", db);
                var locked = await stations.GetForMergeAsync(primary.Id, duplicate.Id);
                locked.Single(station => station.Id == primary.Id)
                    .MergeProfileFrom(locked.Single(station => station.Id == duplicate.Id));
                (await routes.HasStationMergeConflictAsync(duplicate.Id, primary.Id)).Should().BeTrue();
                await transaction.RollbackAsync();
            }

            db.ChangeTracker.Clear();
            var persistedPrimary = await db.Stations.AsNoTracking().SingleAsync(station => station.Id == primary.Id);
            var persistedDuplicate = await db.Stations.AsNoTracking().SingleAsync(station => station.Id == duplicate.Id);
            var persistedRoute = await db.Routes.AsNoTracking().SingleAsync(candidate => candidate.Id == route.Id);
            persistedPrimary.AddressStreet.Should().BeNull();
            persistedDuplicate.DeletedAt.Should().BeNull();
            persistedDuplicate.MergedIntoStationId.Should().BeNull();
            persistedRoute.OriginStationId.Should().Be(primary.Id);
            persistedRoute.DestinationStationId.Should().Be(duplicate.Id);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<MergeSeed> SeedMergeGraphAsync(TripDbContext db)
    {
        var operatorOne = Guid.NewGuid();
        var operatorTwo = Guid.NewGuid();
        var primary = Station.Create(
            "Primary",
            $"primary-{Guid.NewGuid():N}",
            "Primary City",
            "Primary Province",
            contactPhone: "0900000001");
        var duplicate = Station.Create(
            "Duplicate",
            $"duplicate-{Guid.NewGuid():N}",
            "Duplicate City",
            "Duplicate Province",
            addressStreet: "12 Duplicate Street",
            latitude: 10.7m,
            longitude: 106.7m,
            contactEmail: "duplicate@example.com",
            operatingHours: "{\"mon\":\"06:00-22:00\"}",
            facilities: "[\"parking\"]",
            supportsShuttle: true);
        var other = Station.Create("Other", $"other-{Guid.NewGuid():N}", "Other City", "Other Province");
        var oldRedirect = Station.Create("Old", $"old-{Guid.NewGuid():N}", "Old City", "Old Province");
        oldRedirect.MarkMergedInto(duplicate.Id, DateTimeOffset.UtcNow.AddDays(-1));
        var primaryMapping = OperatorStation.Create(operatorOne, primary.Id, contactPhone: "0900000001");
        primaryMapping.Deactivate();
        var duplicateCollision = OperatorStation.Create(
            operatorOne,
            duplicate.Id,
            displayNameOverride: "Duplicate Counter",
            counterLocation: "Gate 2",
            instructions: "Arrive early");
        var duplicateRelink = OperatorStation.Create(operatorTwo, duplicate.Id, displayNameOverride: "Operator Two");
        var originRoute = Route.Create(
            operatorOne,
            "Duplicate to Other",
            duplicate.Id,
            other.Id,
            Money.FromRaw(100_000),
            null,
            null);
        var destinationRoute = Route.Create(
            operatorOne,
            "Other to Duplicate",
            other.Id,
            duplicate.Id,
            Money.FromRaw(100_000),
            null,
            null);
        var alternative = AlternativeRoute.Create(originRoute.Id, "Alternative", duplicate.Id, null, null);
        var vehicleType = VehicleType.Create("STATION_MERGE_TEST", "Station merge test vehicle", 5, 20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(
            operatorOne,
            vehicleType.Id,
            $"MERGE-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            20,
            500m,
            10m);
        var departure = DateTimeOffset.UtcNow.AddHours(2);
        var mainTrip = Domain.Entities.Trip.Create(
            operatorOne,
            originRoute.Id,
            vehicle.Id,
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 5m,
            seatLayoutSnapshotJson: vehicle.SeatLayoutJson);
        var shuttle = ShuttleTrip.Create(
            operatorOne,
            mainTrip.Id,
            duplicate.Id,
            Guid.NewGuid(),
            vehicle.Id,
            departure.AddHours(-1),
            departure.AddMinutes(-30),
            null);

        db.AddRange(
            primary,
            duplicate,
            other,
            oldRedirect,
            primaryMapping,
            duplicateCollision,
            duplicateRelink,
            originRoute,
            destinationRoute,
            alternative,
            vehicleType,
            vehicle,
            mainTrip,
            shuttle);
        await db.SaveChangesAsync();
        return new MergeSeed(
            primary.Id,
            duplicate.Id,
            oldRedirect.Id,
            operatorOne,
            operatorTwo,
            originRoute.Id,
            destinationRoute.Id,
            alternative.Id,
            shuttle.Id);
    }

    private static async Task AssertMergedGraphAsync(TripDbContext db, MergeSeed seed)
    {
        db.ChangeTracker.Clear();
        var primary = await db.Stations.AsNoTracking().SingleAsync(station => station.Id == seed.PrimaryId);
        var duplicate = await db.Stations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(station => station.Id == seed.DuplicateId);
        var oldRedirect = await db.Stations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(station => station.Id == seed.OldRedirectId);
        primary.Name.Should().Be("Primary");
        primary.AddressStreet.Should().Be("12 Duplicate Street");
        primary.ContactPhone.Should().Be("0900000001");
        primary.ContactEmail.Should().Be("duplicate@example.com");
        primary.SupportsShuttle.Should().BeTrue();
        duplicate.MergedIntoStationId.Should().Be(seed.PrimaryId);
        duplicate.DeletedAt.Should().NotBeNull();
        duplicate.IsActive.Should().BeFalse();
        oldRedirect.MergedIntoStationId.Should().Be(seed.PrimaryId);

        var mappings = await db.OperatorStations.AsNoTracking()
            .Where(mapping => mapping.StationId == seed.PrimaryId)
            .OrderBy(mapping => mapping.OperatorId)
            .ToArrayAsync();
        mappings.Should().HaveCount(2);
        var collapsed = mappings.Single(mapping => mapping.OperatorId == seed.OperatorOne);
        collapsed.IsActive.Should().BeTrue();
        collapsed.DisplayNameOverride.Should().Be("Duplicate Counter");
        collapsed.ContactPhone.Should().Be("0900000001");
        mappings.Single(mapping => mapping.OperatorId == seed.OperatorTwo).DisplayNameOverride
            .Should().Be("Operator Two");

        var originRoute = await db.Routes.AsNoTracking().SingleAsync(route => route.Id == seed.OriginRouteId);
        var destinationRoute = await db.Routes.AsNoTracking().SingleAsync(route => route.Id == seed.DestinationRouteId);
        originRoute.OriginStationId.Should().Be(seed.PrimaryId);
        destinationRoute.DestinationStationId.Should().Be(seed.PrimaryId);
        (await db.AlternativeRoutes.AsNoTracking().SingleAsync(route => route.Id == seed.AlternativeRouteId))
            .DestinationStationId.Should().Be(seed.PrimaryId);
        (await db.ShuttleTrips.AsNoTracking().SingleAsync(trip => trip.Id == seed.ShuttleTripId))
            .StationId.Should().Be(seed.PrimaryId);
    }

    private static TRepository CreateRepository<TRepository>(string name, TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            $"VietRide.Trip.Infrastructure.Persistence.Repositories.{name}",
            throwOnError: true)!;
        return (TRepository)Activator.CreateInstance(type, db)!;
    }

    private static TripDbContext CreateDbContext(string databaseName)
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
        return new TripDbContext(options, new SystemClock());
    }

    private static async Task<bool> ColumnExistsAsync(TripDbContext db, string table, string column)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'vietride_trip' AND table_name = @table AND column_name = @column)";
            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "table";
            tableParameter.Value = table;
            command.Parameters.Add(tableParameter);
            var columnParameter = command.CreateParameter();
            columnParameter.ParameterName = "column";
            columnParameter.Value = column;
            command.Parameters.Add(columnParameter);
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
            template = fallback;

        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private sealed record MergeSeed(
        Guid PrimaryId,
        Guid DuplicateId,
        Guid OldRedirectId,
        Guid OperatorOne,
        Guid OperatorTwo,
        Guid OriginRouteId,
        Guid DestinationRouteId,
        Guid AlternativeRouteId,
        Guid ShuttleTripId);
}
