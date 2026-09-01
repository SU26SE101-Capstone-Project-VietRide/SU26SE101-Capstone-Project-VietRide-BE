using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class ShuttleRoutePreviewServiceIntegrationTests
{
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new(StringComparer.Ordinal);

    [Fact]
    public async Task PreviewAsync_InboundLateRisk_PreservesOrderAndWritesNothing()
    {
        var databaseName = $"vietride_trip_shuttle_route_preview_{Guid.NewGuid():N}";
        var now = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        await using var db = CreateDbContext(databaseName, new FrozenClock(now));

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db, now.AddHours(2));
            var firstBookingId = Guid.NewGuid();
            var secondBookingId = Guid.NewGuid();
            db.ShuttlePassengers.AddRange(
                CreateManifest(seed.MainTripId, firstBookingId, 10.71m, 106.61m),
                CreateManifest(seed.MainTripId, secondBookingId, 10.72m, 106.62m));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var before = await SnapshotCountsAsync(db);
            var estimator = new StubRouteEstimator(TimeSpan.FromMinutes(40));
            var service = CreateService(db, estimator, stopServiceMinutes: 5);

            var result = await service.PreviewAsync(new ShuttleRoutePreviewInput(
                seed.OperatorId,
                seed.MainTripId,
                ShuttleTrip.InboundDirection,
                now.AddHours(1),
                [secondBookingId, firstBookingId]));

            result.Status.Should().Be(ShuttleRoutePreviewStatuses.LateRisk);
            result.EstimatedFinishAt.Should().Be(now.AddHours(1).AddMinutes(50));
            result.HardCutoffAt.Should().BeCloseTo(now.AddHours(1).AddMinutes(30), TimeSpan.FromMilliseconds(1));
            result.DelayMinutes.Should().Be(20);
            result.WarningCode.Should().Be("SHUTTLE_LATE_RISK");
            result.LateRiskBlocksCreate.Should().BeFalse();
            result.Basis.Should().Be("GOONG");
            estimator.Calls.Should().ContainSingle();
            estimator.Calls[0].Origin.Should().Be(new ShuttleRouteCoordinate(10.72m, 106.62m));
            estimator.Calls[0].Destinations.Should().Equal(
                new ShuttleRouteCoordinate(10.71m, 106.61m),
                new ShuttleRouteCoordinate(10.7769m, 106.7009m));

            estimator.Result = TimeSpan.FromMinutes(19).Add(TimeSpan.FromSeconds(59));
            var safe = await service.PreviewAsync(new ShuttleRoutePreviewInput(
                seed.OperatorId,
                seed.MainTripId,
                ShuttleTrip.InboundDirection,
                now.AddHours(1),
                [secondBookingId, firstBookingId]));

            safe.Status.Should().Be(ShuttleRoutePreviewStatuses.Safe);
            safe.DelayMinutes.Should().Be(0);
            safe.WarningCode.Should().BeNull();

            estimator.Result = TimeSpan.FromMinutes(20).Add(TimeSpan.FromSeconds(1));
            var oneSecondLate = await service.PreviewAsync(new ShuttleRoutePreviewInput(
                seed.OperatorId,
                seed.MainTripId,
                ShuttleTrip.InboundDirection,
                now.AddHours(1),
                [secondBookingId, firstBookingId]));

            oneSecondLate.Status.Should().Be(ShuttleRoutePreviewStatuses.LateRisk);
            oneSecondLate.DelayMinutes.Should().Be(1);
            (await SnapshotCountsAsync(db)).Should().Be(before);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task PreviewAsync_UnknownAndOutboundNotApplicable_DoNotCallOrWriteUnexpectedly()
    {
        var databaseName = $"vietride_trip_shuttle_route_preview_status_{Guid.NewGuid():N}";
        var now = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        await using var db = CreateDbContext(databaseName, new FrozenClock(now));

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db, now.AddHours(2));
            var bookingId = Guid.NewGuid();
            db.ShuttlePassengers.Add(CreateManifest(seed.MainTripId, bookingId, 10.71m, 106.61m));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var estimator = new StubRouteEstimator(null);
            var service = CreateService(db, estimator, stopServiceMinutes: 5);

            var unknown = await service.PreviewAsync(new ShuttleRoutePreviewInput(
                seed.OperatorId,
                seed.MainTripId,
                ShuttleTrip.InboundDirection,
                now.AddHours(1),
                [bookingId]));
            var notApplicable = await service.PreviewAsync(new ShuttleRoutePreviewInput(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ShuttleTrip.OutboundDirection,
                now,
                [Guid.NewGuid()]));

            unknown.Status.Should().Be(ShuttleRoutePreviewStatuses.Unknown);
            unknown.EstimatedFinishAt.Should().BeNull();
            unknown.HardCutoffAt.Should().BeCloseTo(
                now.AddHours(1).AddMinutes(30),
                TimeSpan.FromMilliseconds(1));
            unknown.DelayMinutes.Should().BeNull();
            unknown.Basis.Should().BeNull();
            notApplicable.Status.Should().Be(ShuttleRoutePreviewStatuses.NotApplicable);
            notApplicable.HardCutoffAt.Should().BeNull();
            estimator.Calls.Should().ContainSingle();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task PreviewAsync_EnforcesTenantAndRejectsStaleBookingGroup()
    {
        var databaseName = $"vietride_trip_shuttle_route_preview_guard_{Guid.NewGuid():N}";
        var now = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        await using var db = CreateDbContext(databaseName, new FrozenClock(now));

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db, now.AddHours(2));
            var bookingId = Guid.NewGuid();
            var manifest = CreateManifest(seed.MainTripId, bookingId, 10.71m, 106.61m);
            manifest.Cancel("Passenger cancelled");
            db.ShuttlePassengers.Add(manifest);
            await db.SaveChangesAsync();
            var estimator = new StubRouteEstimator(TimeSpan.FromMinutes(10));
            var service = CreateService(db, estimator, stopServiceMinutes: 5);

            var wrongTenant = () => service.PreviewAsync(new ShuttleRoutePreviewInput(
                Guid.NewGuid(),
                seed.MainTripId,
                ShuttleTrip.InboundDirection,
                now,
                [bookingId]));
            var stale = () => service.PreviewAsync(new ShuttleRoutePreviewInput(
                seed.OperatorId,
                seed.MainTripId,
                ShuttleTrip.InboundDirection,
                now,
                [bookingId]));

            (await wrongTenant.Should().ThrowAsync<CodedNotFoundException>())
                .Which.ErrorCode.Should().Be("TRIP_NOT_FOUND");
            (await stale.Should().ThrowAsync<CodedConflictException>())
                .Which.ErrorCode.Should().Be("SHUTTLE_REQUEST_SET_CHANGED");
            estimator.Calls.Should().BeEmpty();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static IShuttleRoutePreviewService CreateService(
        TripDbContext db,
        IShuttleRouteEstimator estimator,
        int stopServiceMinutes)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Services.ShuttleRoutePreviewService",
            throwOnError: true)!;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SHUTTLE_STOP_SERVICE_MINUTES"] = stopServiceMinutes.ToString(),
            })
            .Build();
        return (IShuttleRoutePreviewService)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db, estimator, configuration],
            culture: null)!;
    }

    private static async Task<Seed> SeedAsync(TripDbContext db, DateTimeOffset departure)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Shuttle Origin",
            $"shuttle-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ward 1",
            latitude: 10.7769m,
            longitude: 106.7009m,
            supportsShuttle: true);
        var destination = Station.Create(
            "Shuttle Destination",
            $"shuttle-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Ward 2",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = Domain.Entities.Route.Create(
            operatorId,
            "Shuttle route preview integration",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            180);
        var vehicleType = VehicleType.Create("SHUTTLE_ROUTE_PREVIEW", "Preview vehicle", 5, 20);
        var layout = CreateSeatLayout();
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"PREV-{Guid.NewGuid():N}"[..20],
            layout,
            20,
            500m,
            10m);
        var trip = Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
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
            seatLayoutSnapshotJson: layout);
        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        return new Seed(operatorId, trip.Id);
    }

    private static ShuttlePassenger CreateManifest(
        Guid mainTripId,
        Guid bookingId,
        decimal latitude,
        decimal longitude) =>
        ShuttlePassenger.Request(
            mainTripId,
            bookingId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"Pickup {bookingId:N}",
            latitude,
            longitude,
            roadDistanceMeters: 1_000);

    private static JsonElement CreateSeatLayout() => JsonSerializer.SerializeToElement(new
    {
        version = 1,
        vehicleTypeCode = "SHUTTLE_ROUTE_PREVIEW",
        totalSeats = 1,
        rows = 1,
        cols = 1,
        decks = 1,
        aisles = Array.Empty<object>(),
        seats = new[]
        {
            new
            {
                seatNumber = "A01",
                row = 1,
                col = 1,
                deck = 1,
                type = "STANDARD",
                isWindow = true,
                isAisle = false,
                disabled = false,
            },
        },
    });

    private static async Task<DatabaseCounts> SnapshotCountsAsync(TripDbContext db) =>
        new(
            await db.ShuttleTrips.CountAsync(),
            await db.ShuttlePassengers.CountAsync(),
            await db.ResourceReservations.CountAsync(),
            await db.OutboxEvents.CountAsync());

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

    private sealed record Seed(Guid OperatorId, Guid MainTripId);

    private sealed record DatabaseCounts(
        int ShuttleTrips,
        int ShuttlePassengers,
        int ResourceReservations,
        int OutboxEvents);

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class StubRouteEstimator : IShuttleRouteEstimator
    {
        public StubRouteEstimator(TimeSpan? result)
        {
            Result = result;
        }

        public List<RouteEstimatorCall> Calls { get; } = [];

        public TimeSpan? Result { get; set; }

        public Task<TimeSpan?> EstimateDurationAsync(
            ShuttleRouteCoordinate origin,
            IReadOnlyList<ShuttleRouteCoordinate> destinations,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RouteEstimatorCall(origin, destinations.ToArray()));
            return Task.FromResult(Result);
        }
    }

    private sealed record RouteEstimatorCall(
        ShuttleRouteCoordinate Origin,
        IReadOnlyList<ShuttleRouteCoordinate> Destinations);
}
