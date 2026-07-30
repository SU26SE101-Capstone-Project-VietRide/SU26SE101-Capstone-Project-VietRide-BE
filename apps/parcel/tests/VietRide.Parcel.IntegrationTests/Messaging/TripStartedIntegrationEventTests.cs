using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Messaging;

public sealed class TripStartedIntegrationEventTests
{
    [Fact]
    public async Task FirstDelivery_UpdatesOnlyLoadedParcels_AndDuplicatePreservesTimestamps()
    {
        var databaseName = $"vietride_parcel_trip_started_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var dbContext = CreateDbContext(dataSource);
            await dbContext.Database.MigrateAsync();

            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
            appliedMigrations.Should().Contain("20260702000000_AddLoadedByUserId");
            appliedMigrations.Should().Contain("20260714113506_PreserveExplicitParcelUpdatedAt");
            (await IsUpdatedAtTriggerActiveAsync(dbContext)).Should().BeTrue();

            var tripId = Guid.NewGuid();
            var otherTripId = Guid.NewGuid();
            var baseline = new DateTimeOffset(2026, 7, 13, 7, 0, 0, TimeSpan.Zero);
            var actualDepartureTime = new DateTimeOffset(2026, 7, 14, 8, 15, 30, TimeSpan.FromHours(7));
            var rows = new[]
            {
                CreateParcel("VRP-START-LOADED-1", tripId),
                CreateParcel("VRP-START-LOADED-2", tripId),
                CreateParcel("VRP-START-PENDING", tripId),
                CreateParcel("VRP-START-PAYMENT", tripId),
                CreateParcel("VRP-START-TRANSIT", tripId),
                CreateParcel("VRP-START-TERMINAL", tripId),
                CreateParcel("VRP-START-OTHER", otherTripId),
            };

            dbContext.Parcels.AddRange(rows);
            await dbContext.SaveChangesAsync();

            await SetStatusAsync(dbContext, rows[0].Id, ParcelStatus.LOADED, baseline);
            await SetStatusAsync(dbContext, rows[1].Id, ParcelStatus.LOADED, baseline);
            await SetStatusAsync(dbContext, rows[2].Id, ParcelStatus.PENDING, baseline);
            await SetStatusAsync(dbContext, rows[3].Id, ParcelStatus.PENDING_PAYMENT, baseline);
            await SetStatusAsync(dbContext, rows[4].Id, ParcelStatus.IN_TRANSIT, baseline);
            await SetStatusAsync(dbContext, rows[5].Id, ParcelStatus.DELIVERY_CONFIRMED, baseline);
            await SetStatusAsync(dbContext, rows[6].Id, ParcelStatus.LOADED, baseline);
            dbContext.ChangeTracker.Clear();

            var handler = new HandleTripStartedCommandHandler(CreateRepository(dbContext));
            var command = new HandleTripStartedCommand(tripId, actualDepartureTime);

            var firstChanged = await handler.Handle(command, CancellationToken.None);
            var afterFirst = await ReadStateAsync(dbContext, rows);
            var duplicateChanged = await handler.Handle(command, CancellationToken.None);
            var afterDuplicate = await ReadStateAsync(dbContext, rows);

            firstChanged.Should().Be(2);
            duplicateChanged.Should().Be(0);
            afterFirst[rows[0].Id].Should().Be((ParcelStatus.IN_TRANSIT, actualDepartureTime));
            afterFirst[rows[1].Id].Should().Be((ParcelStatus.IN_TRANSIT, actualDepartureTime));
            afterFirst[rows[2].Id].Should().Be((ParcelStatus.PENDING, baseline));
            afterFirst[rows[3].Id].Should().Be((ParcelStatus.PENDING_PAYMENT, baseline));
            afterFirst[rows[4].Id].Should().Be((ParcelStatus.IN_TRANSIT, baseline));
            afterFirst[rows[5].Id].Should().Be((ParcelStatus.DELIVERY_CONFIRMED, baseline));
            afterFirst[rows[6].Id].Should().Be((ParcelStatus.LOADED, baseline));
            afterDuplicate.Should().BeEquivalentTo(afterFirst);

            var beforeImplicitTimestampUpdate = DateTimeOffset.UtcNow.AddSeconds(-1);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET description = description
                WHERE id = {rows[5].Id};
                """);
            var triggerAssignedTimestamp = await dbContext.Parcels
                .AsNoTracking()
                .Where(parcel => parcel.Id == rows[5].Id)
                .Select(parcel => parcel.UpdatedAt)
                .SingleAsync();
            triggerAssignedTimestamp.Should().BeAfter(beforeImplicitTimestampUpdate);

            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260709083242_AddParcelCapacityVolumeDimWeight");
            (await dbContext.Database.GetAppliedMigrationsAsync())
                .Should().NotContain("20260714113506_PreserveExplicitParcelUpdatedAt");

            await dbContext.Database.MigrateAsync();
            (await dbContext.Database.GetAppliedMigrationsAsync())
                .Should().Contain("20260714113506_PreserveExplicitParcelUpdatedAt");
            (await IsUpdatedAtTriggerActiveAsync(dbContext)).Should().BeTrue();
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ParcelEntity CreateParcel(string parcelCode, Guid tripId)
        => ParcelEntity.CreatePendingPayment(
            parcelCode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            Guid.NewGuid(),
            tripId,
            null,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static async Task SetStatusAsync(
        ParcelDbContext dbContext,
        Guid parcelId,
        ParcelStatus status,
        DateTimeOffset updatedAt)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = CAST({status.ToString()} AS vietride_parcel.parcel_status),
                updated_at = {updatedAt}
            WHERE id = {parcelId};
            """);
    }

    private static async Task<Dictionary<Guid, (ParcelStatus Status, DateTimeOffset UpdatedAt)>> ReadStateAsync(
        ParcelDbContext dbContext,
        IReadOnlyCollection<ParcelEntity> rows)
    {
        var ids = rows.Select(row => row.Id).ToArray();
        return await dbContext.Parcels
            .AsNoTracking()
            .Where(parcel => ids.Contains(parcel.Id))
            .ToDictionaryAsync(parcel => parcel.Id, parcel => new ValueTuple<ParcelStatus, DateTimeOffset>(
                parcel.Status,
                parcel.UpdatedAt));
    }

    private static IParcelRepository CreateRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;

        return (IParcelRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static async Task<bool> IsUpdatedAtTriggerActiveAsync(ParcelDbContext dbContext)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_trigger trigger
                JOIN pg_class relation ON relation.oid = trigger.tgrelid
                JOIN pg_namespace schema ON schema.oid = relation.relnamespace
                WHERE schema.nspname = 'vietride_parcel'
                  AND relation.relname = 'parcels'
                  AND trigger.tgname = 'trg_parcels_updated_at'
                  AND NOT trigger.tgisinternal
                  AND trigger.tgenabled <> 'D');
            """;

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        return (bool)(await command.ExecuteScalarAsync())!;
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
        const string defaultConnectionString =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
            ? defaultConnectionString
            : configuredConnectionString;

        connectionString = connectionString.Replace(
            "{databaseName}",
            databaseName,
            StringComparison.OrdinalIgnoreCase);
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);", connection);
        await command.ExecuteNonQueryAsync();
    }
}
