using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Internal.Trips;

public sealed class Day29CargoNearFullOutboxIntegrationTests
{
    internal const string ScratchDatabasePrefix = "vietride_day29_cargo_outbox_";
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ThresholdCrossing_CommitsCounterLedgerAndPendingOutboxAtomically()
    {
        var databaseName = $"{ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedTripAsync(db);
            var handler = CreateHandler(db, new IntegrationEventOutbox(new OutboxStore(db, new FixedClock())));

            await handler.Handle(CreateLoad(seed.TripId, Guid.NewGuid(), 70m), CancellationToken.None);
            var crossingParcelId = Guid.NewGuid();
            await handler.Handle(CreateLoad(seed.TripId, crossingParcelId, 10m), CancellationToken.None);

            await using var assertionDb = CreateDbContext(databaseName);
            var trip = await assertionDb.Trips.AsNoTracking().SingleAsync(item => item.Id == seed.TripId);
            var ledger = await assertionDb.TripCargoParcels.AsNoTracking()
                .Where(item => item.TripId == seed.TripId)
                .ToArrayAsync();
            var outboxRows = await assertionDb.OutboxEvents.AsNoTracking()
                .Where(item => item.EventType == CargoThresholdCrossedIntegrationEvent.EventTypeValue)
                .ToArrayAsync();

            trip.TotalLoadedWeightKg.Should().Be(80m);
            trip.ReservedParcelWeightKg.Should().Be(0m);
            ledger.Should().HaveCount(2).And.OnlyContain(item => item.State == TripCargoParcel.LoadedState);
            ledger.Should().ContainSingle(item => item.ParcelId == crossingParcelId);
            outboxRows.Should().ContainSingle();
            outboxRows[0].Status.Should().Be(OutboxEventStatus.PENDING);
            outboxRows[0].PublishedAt.Should().BeNull();

            using var payload = JsonDocument.Parse(outboxRows[0].Payload);
            payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
                ["eventId", "occurredAt", "tripId", "operatorId", "loadedWeightKg", "maxCargoWeightKg", "percentFull"]);
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outboxRows[0].Id);
            payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
            payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(seed.TripId);
            payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(seed.OperatorId);
            payload.RootElement.GetProperty("loadedWeightKg").GetDecimal().Should().Be(80m);
            payload.RootElement.GetProperty("maxCargoWeightKg").GetDecimal().Should().Be(100m);
            payload.RootElement.GetProperty("percentFull").GetDecimal().Should().Be(80m);
        }
        finally
        {
            await DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    [Fact]
    public async Task ForcedCommitFailure_RollsBackCounterLedgerAndOutbox()
    {
        var databaseName = $"{ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedTripAsync(db);
            var durableOutbox = new IntegrationEventOutbox(new OutboxStore(db, new FixedClock()));
            var handler = CreateHandler(db, new ThrowAfterStagingOutbox(durableOutbox));

            var action = () => handler.Handle(CreateLoad(seed.TripId, Guid.NewGuid(), 80m), CancellationToken.None);
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("staged outbox failure");

            await using var assertionDb = CreateDbContext(databaseName);
            var trip = await assertionDb.Trips.AsNoTracking().SingleAsync(item => item.Id == seed.TripId);
            trip.TotalLoadedWeightKg.Should().Be(0m);
            trip.ReservedParcelWeightKg.Should().Be(0m);
            var rolledBackLedger = await assertionDb.TripCargoParcels.AsNoTracking()
                .Where(item => item.TripId == seed.TripId)
                .ToArrayAsync();
            var rolledBackOutbox = await assertionDb.OutboxEvents.AsNoTracking()
                .Where(item => item.EventType == CargoThresholdCrossedIntegrationEvent.EventTypeValue)
                .ToArrayAsync();
            rolledBackLedger.Should().BeEmpty();
            rolledBackOutbox.Should().BeEmpty();
        }
        finally
        {
            await DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    internal static CargoMutationCommandHandler CreateHandler(TripDbContext db, IIntegrationEventOutbox outbox) =>
        new(CreateRepository(db), outbox, new EfUnitOfWork(db), new FixedClock());

    private static ITripRepository CreateRepository(TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
            throwOnError: true)!;
        return (ITripRepository)Activator.CreateInstance(type, db)!;
    }

    internal static CargoMutationCommand CreateLoad(Guid tripId, Guid parcelId, decimal weightKg) =>
        new(tripId, parcelId, weightKg, 1m, false, "load");

    internal static async Task<Seed> SeedTripAsync(TripDbContext db)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Day 29 cargo origin", $"day29-origin-{Guid.NewGuid():N}", "Ho Chi Minh City", "Ho Chi Minh City");
        var destination = Station.Create("Day 29 cargo destination", $"day29-destination-{Guid.NewGuid():N}", "Da Nang", "Da Nang");
        var route = VietRide.Trip.Domain.Entities.Route.Create(operatorId, "Day 29 cargo route", origin.Id, destination.Id, Money.FromRaw(100_000), 100m, 240);
        var vehicleType = VehicleType.Create($"DAY29_{Guid.NewGuid():N}", "Day 29 cargo vehicle", null, 1);
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"D29-{Guid.NewGuid():N}"[..20],
            JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "DAY29",
                totalSeats = 1,
                rows = 1,
                cols = 1,
                decks = 1,
                aisles = Array.Empty<object>(),
                seats = new[] { new { seatNumber = "A01", row = 1, col = 1, deck = 1, type = "STANDARD", isWindow = true, isAisle = false, disabled = false } },
            }),
            1,
            100m,
            10m);
        var departure = Now.AddDays(1);
        var trip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(4),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            100m,
            10m,
            0m);

        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seed(operatorId, trip.Id);
    }

    internal static TripDbContext CreateDbContext(string databaseName)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        dataSourceBuilder.MapEnum<OutboxEventStatus>($"{TripDbContext.SchemaName}.outbox_event_status", new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(dataSourceBuilder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(dataSourceBuilder.Build(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;
        return new TripDbContext(options, new FixedClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        var expanded = template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
        return new NpgsqlConnectionStringBuilder(expanded) { Database = databaseName }.ConnectionString;
    }

    internal static async Task DeleteScratchDatabaseAsync(TripDbContext db, string databaseName)
    {
        var connectedDatabase = db.Database.GetDbConnection().Database;
        if (!databaseName.StartsWith(ScratchDatabasePrefix, StringComparison.Ordinal)
            || !string.Equals(connectedDatabase, databaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to delete non-scratch database '{connectedDatabase}'.");
        }

        await db.Database.EnsureDeletedAsync();
    }

    private sealed class ThrowAfterStagingOutbox(IIntegrationEventOutbox inner) : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task EnqueueAsync(Guid eventId, string eventType, string payloadJson, CancellationToken cancellationToken = default)
        {
            await inner.EnqueueAsync(eventId, eventType, payloadJson, cancellationToken);
            throw new InvalidOperationException("staged outbox failure");
        }
    }

    internal sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    internal sealed record Seed(Guid OperatorId, Guid TripId);
}
