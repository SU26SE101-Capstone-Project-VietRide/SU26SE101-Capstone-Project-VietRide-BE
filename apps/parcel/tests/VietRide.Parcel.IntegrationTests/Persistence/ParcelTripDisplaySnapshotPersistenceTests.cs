using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.DisplaySnapshots;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class ParcelTripDisplaySnapshotPersistenceTests
{
    [Fact]
    public async Task Backfill_IsBoundedCasAndNeverOverwritesOrMixesSnapshotValues()
    {
        var databaseName = $"vietride_parcel_ui11_snapshot_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var parcel = CreateParcel();
            var newWrite = CreateParcel();
            var newWriteSnapshot = Update(
                newWrite.Id,
                newWrite.TripId,
                "New Route",
                "New Origin",
                "New Destination",
                "51N-00001");
            newWrite.CaptureTripDisplaySnapshot(
                newWriteSnapshot.Summary.Route.RouteId,
                newWriteSnapshot.Summary.Route.Name,
                newWriteSnapshot.Summary.Route.OriginName,
                newWriteSnapshot.Summary.Route.DestinationName,
                newWriteSnapshot.Summary.Vehicle.VehicleId,
                newWriteSnapshot.Summary.Vehicle.LicensePlate);
            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.AddRange(parcel, newWrite);
                await seedContext.SaveChangesAsync();
            }

            await AssertNewWriteSnapshotPersistedAsync(dataSource, newWrite, newWriteSnapshot);

            await using (var candidateContext = CreateDbContext(dataSource))
            {
                var candidates = await CreateRepository(candidateContext)
                    .ListTripDisplaySnapshotBackfillCandidatesAsync(500, CancellationToken.None);
                candidates.Should().ContainSingle()
                    .Which.Should().Be(new ParcelTripDisplaySnapshotCandidate(parcel.Id, parcel.TripId));
            }

            await AssertTripAssignmentCasRejectsStaleSummaryAsync(dataSource);

            var first = Update(parcel.Id, parcel.TripId, "Route A", "Origin A", "Destination A", "51A-11111");
            var second = Update(parcel.Id, parcel.TripId, "Route B", "Origin B", "Destination B", "51B-22222");
            await using var firstContext = CreateDbContext(dataSource);
            await using var secondContext = CreateDbContext(dataSource);
            var results = await Task.WhenAll(
                CreateRepository(firstContext).ApplyTripDisplaySnapshotBackfillAsync([first], CancellationToken.None),
                CreateRepository(secondContext).ApplyTripDisplaySnapshotBackfillAsync([second], CancellationToken.None));

            results.Sum().Should().Be(1);

            await using (var readContext = CreateDbContext(dataSource))
            {
                var persisted = await readContext.Parcels.AsNoTracking().SingleAsync(item => item.Id == parcel.Id);
                var isFirst = persisted.TripSnapshotRouteId == first.Summary.Route.RouteId;
                var winner = isFirst ? first : second;
                persisted.TripSnapshotRouteId.Should().Be(winner.Summary.Route.RouteId);
                persisted.TripSnapshotRouteName.Should().Be(winner.Summary.Route.Name);
                persisted.TripSnapshotOriginStationName.Should().Be(winner.Summary.Route.OriginName);
                persisted.TripSnapshotDestinationStationName.Should().Be(winner.Summary.Route.DestinationName);
                persisted.TripSnapshotVehicleId.Should().Be(winner.Summary.Vehicle.VehicleId);
                persisted.TripSnapshotVehicleLicensePlate.Should().Be(winner.Summary.Vehicle.LicensePlate);

                var repository = CreateRepository(readContext);
                (await repository.ListTripDisplaySnapshotBackfillCandidatesAsync(100, CancellationToken.None))
                    .Should().NotContain(candidate => candidate.ParcelId == parcel.Id);
                (await repository.ApplyTripDisplaySnapshotBackfillAsync(
                    [isFirst ? second : first],
                    CancellationToken.None)).Should().Be(0);
            }

            await AssertSnapshotColumnsAreNullableAsync(dataSource);
            await AssertCandidateBatchIsCappedAsync(dataSource);
            await AssertMigrationRoundTripAsync(dataSource);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ParcelTripDisplaySnapshotUpdate Update(
        Guid parcelId,
        Guid tripId,
        string routeName,
        string originName,
        string destinationName,
        string licensePlate)
        => new(
            parcelId,
            tripId,
            new TripSummarySnapshot(
                tripId,
                "COMPLETED",
                DateTimeOffset.UtcNow.AddHours(-8),
                DateTimeOffset.UtcNow,
                new TripRouteSummarySnapshot(Guid.NewGuid(), routeName, originName, destinationName),
                new TripVehicleSummarySnapshot(Guid.NewGuid(), licensePlate, "ACTIVE")));

    private static async Task AssertTripAssignmentCasRejectsStaleSummaryAsync(NpgsqlDataSource dataSource)
    {
        var parcel = CreateParcel();
        var originalTripId = parcel.TripId;
        await using (var seedContext = CreateDbContext(dataSource))
        {
            seedContext.Parcels.Add(parcel);
            await seedContext.SaveChangesAsync();
            await seedContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET trip_id = {Guid.NewGuid()}
                WHERE id = {parcel.Id};
                """);
        }

        await using var applyContext = CreateDbContext(dataSource);
        var affected = await CreateRepository(applyContext).ApplyTripDisplaySnapshotBackfillAsync(
            [Update(parcel.Id, originalTripId, "Stale", "Stale", "Stale", "00A-00000")],
            CancellationToken.None);
        affected.Should().Be(0);

        var persisted = await applyContext.Parcels.AsNoTracking().SingleAsync(item => item.Id == parcel.Id);
        persisted.TripSnapshotRouteId.Should().BeNull();
        persisted.TripSnapshotVehicleId.Should().BeNull();
    }

    private static async Task AssertNewWriteSnapshotPersistedAsync(
        NpgsqlDataSource dataSource,
        ParcelEntity parcel,
        ParcelTripDisplaySnapshotUpdate expected)
    {
        await using var context = CreateDbContext(dataSource);
        var persisted = await context.Parcels.AsNoTracking().SingleAsync(item => item.Id == parcel.Id);
        persisted.TripSnapshotRouteId.Should().Be(expected.Summary.Route.RouteId);
        persisted.TripSnapshotRouteName.Should().Be(expected.Summary.Route.Name);
        persisted.TripSnapshotOriginStationName.Should().Be(expected.Summary.Route.OriginName);
        persisted.TripSnapshotDestinationStationName.Should().Be(expected.Summary.Route.DestinationName);
        persisted.TripSnapshotVehicleId.Should().Be(expected.Summary.Vehicle.VehicleId);
        persisted.TripSnapshotVehicleLicensePlate.Should().Be(expected.Summary.Vehicle.LicensePlate);
    }

    private static async Task AssertCandidateBatchIsCappedAsync(NpgsqlDataSource dataSource)
    {
        await using var context = CreateDbContext(dataSource);
        context.Parcels.AddRange(Enumerable.Range(0, 101).Select(_ => CreateParcel()));
        await context.SaveChangesAsync();

        var candidates = await CreateRepository(context)
            .ListTripDisplaySnapshotBackfillCandidatesAsync(500, CancellationToken.None);
        candidates.Should().HaveCount(100);
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            ($"VRP-UI11-{Guid.NewGuid():N}")[..30],
            Guid.NewGuid(),
            null,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static async Task AssertSnapshotColumnsAreNullableAsync(NpgsqlDataSource dataSource)
    {
        const string sql = """
            SELECT column_name, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'vietride_parcel'
              AND table_name = 'parcels'
              AND column_name LIKE 'trip_snapshot_%';
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, string>();
        while (await reader.ReadAsync())
            columns[reader.GetString(0)] = reader.GetString(1);

        columns.Should().HaveCount(6);
        columns.Values.Should().OnlyContain(nullable => nullable == "YES");
    }

    private static async Task AssertMigrationRoundTripAsync(NpgsqlDataSource dataSource)
    {
        await using var context = CreateDbContext(dataSource);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260728094014_AddParcelEvidencePhotos");

        await using (var command = dataSource.CreateCommand("""
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'vietride_parcel'
              AND table_name = 'parcels'
              AND column_name LIKE 'trip_snapshot_%';
            """))
        {
            Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(0);
        }

        await migrator.MigrateAsync();
        await AssertSnapshotColumnsAreNullableAsync(dataSource);
    }

    private static IParcelRepository CreateRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;
        return (IParcelRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = ParcelIntegrationDbContextOptions.Create(dataSource);
        return new ParcelDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(
            connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
