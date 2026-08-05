using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.Internal.Trips;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.IntegrationTests.Internal.OperatorAnalytics;

public sealed class OperatorAnalyticsRepositoryTests
{
    [Fact]
    public async Task PostgreSqlQueriesHonorCurrentVehicleAndIctRouteSemanticsWithOneSqlEach()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var setupDb = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await setupDb.Database.MigrateAsync();
            var seed = await SeedAsync(setupDb);
            var interceptor = new CountingCommandInterceptor();
            await using var queryDb = CreateCountingDbContext(
                CreateConnectionString(databaseName),
                interceptor);
            var repository = CreateRepository(queryDb);

            var vehicleCounts = await repository.GetVehicleCountsAsync(
                [seed.OperatorId, Guid.NewGuid()],
                CancellationToken.None);

            vehicleCounts.Should().ContainSingle().Which.Should().Be(
                new OperatorVehicleCountReadModel(seed.OperatorId, 2));
            interceptor.ReaderCount.Should().Be(1);

            interceptor.Reset();
            var performance = await repository.GetRoutePerformanceAsync(
                seed.OperatorId,
                DateTimeOffset.Parse("2026-06-30T17:00:00Z"),
                DateTimeOffset.Parse("2026-07-31T17:00:00Z"),
                CancellationToken.None);

            performance.Should().ContainSingle().Which.Should().Be(
                new OperatorRoutePerformanceReadModel(
                    seed.RouteId,
                    "A historical route",
                    "Historical origin",
                    "Historical destination",
                    2,
                    1));
            interceptor.ReaderCount.Should().Be(1);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(setupDb, databaseName);
        }
    }

    private static IOperatorAnalyticsRepository CreateRepository(TripDbContext dbContext)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.OperatorAnalyticsRepository",
            throwOnError: true)!;
        return (IOperatorAnalyticsRepository)Activator.CreateInstance(type, dbContext)!;
    }

    private static TripDbContext CreateCountingDbContext(
        string connectionString,
        DbCommandInterceptor interceptor)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(dataSourceBuilder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(
                dataSourceBuilder.Build(),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .AddInterceptors(interceptor)
            .Options;
        return new TripDbContext(options, new Day29CargoNearFullOutboxIntegrationTests.FixedClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
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

    private static async Task<Seed> SeedAsync(TripDbContext dbContext)
    {
        var operatorId = Guid.NewGuid();
        var foreignOperatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Historical origin",
            $"ui19-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City");
        var destination = Station.Create(
            "Historical destination",
            $"ui19-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong");
        var route = Route.Create(
            operatorId,
            "A historical route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            100m,
            180);
        var foreignRoute = Route.Create(
            foreignOperatorId,
            "Foreign route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            100m,
            180);
        var vehicleType = VehicleType.Create($"UI19_{Guid.NewGuid():N}", "UI-19 vehicle", null, 1);
        var layout = CreateSeatLayout();
        var activeVehicle = CreateVehicle(operatorId, vehicleType.Id, layout);
        var inactiveVehicle = CreateVehicle(operatorId, vehicleType.Id, layout);
        inactiveVehicle.ChangeStatus(VehicleStatus.MAINTENANCE);
        inactiveVehicle.Deactivate();
        var deletedVehicle = CreateVehicle(operatorId, vehicleType.Id, layout);
        var foreignVehicle = CreateVehicle(foreignOperatorId, vehicleType.Id, layout);

        var includedCompleted = CreateTrip(
            operatorId,
            route.Id,
            activeVehicle.Id,
            DateTimeOffset.Parse("2026-06-30T17:00:00Z"));
        includedCompleted.MarkBoarding(includedCompleted.DepartureDateTime.AddMinutes(-30));
        includedCompleted.Start(includedCompleted.DepartureDateTime);
        includedCompleted.CompleteAutomatically(includedCompleted.DepartureDateTime.AddHours(1));
        var includedScheduled = CreateTrip(
            operatorId,
            route.Id,
            activeVehicle.Id,
            DateTimeOffset.Parse("2026-07-15T04:00:00Z"));
        var beforeMonth = CreateTrip(
            operatorId,
            route.Id,
            activeVehicle.Id,
            DateTimeOffset.Parse("2026-06-30T16:59:59Z"));
        var exclusiveEnd = CreateTrip(
            operatorId,
            route.Id,
            activeVehicle.Id,
            DateTimeOffset.Parse("2026-07-31T17:00:00Z"));
        var foreignTrip = CreateTrip(
            foreignOperatorId,
            foreignRoute.Id,
            foreignVehicle.Id,
            DateTimeOffset.Parse("2026-07-15T05:00:00Z"));

        dbContext.AddRange(
            origin,
            destination,
            route,
            foreignRoute,
            vehicleType,
            activeVehicle,
            inactiveVehicle,
            deletedVehicle,
            foreignVehicle,
            includedCompleted,
            includedScheduled,
            beforeMonth,
            exclusiveEnd,
            foreignTrip);
        await dbContext.SaveChangesAsync();

        var deletedAt = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        deletedVehicle.SoftDelete(deletedAt);
        route.SoftDelete(deletedAt);
        origin.SoftDelete(deletedAt);
        destination.SoftDelete(deletedAt);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return new Seed(operatorId, route.Id);
    }

    private static Vehicle CreateVehicle(Guid operatorId, Guid vehicleTypeId, JsonElement layout)
        => Vehicle.Create(
            operatorId,
            vehicleTypeId,
            $"U19-{Guid.NewGuid():N}"[..20],
            layout,
            1,
            null,
            null);

    private static TripEntity CreateTrip(
        Guid operatorId,
        Guid routeId,
        Guid vehicleId,
        DateTimeOffset departure)
        => TripEntity.Create(
            operatorId,
            routeId,
            vehicleId,
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            null,
            0m);

    private static JsonElement CreateSeatLayout()
        => JsonSerializer.SerializeToElement(new
        {
            version = 1,
            vehicleTypeCode = "UI19",
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

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCount++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public void Reset() => ReaderCount = 0;
    }

    private sealed record Seed(Guid OperatorId, Guid RouteId);
}
