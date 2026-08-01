using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class Day35ParcelTransferClaimPersistenceTests
{
    private const string PreviousMigration =
        "20260730024750_HashedParcelDeliveryTokenHistory";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_UpDownAndReapply_TracksCanonicalClaimSchema()
    {
        var databaseName = $"vietride_parcel_day35_migration_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var dbContext = CreateDbContext(dataSource);
            var migrator = dbContext.GetService<IMigrator>();

            await migrator.MigrateAsync(PreviousMigration);
            (await ColumnExistsAsync(
                dbContext,
                "transfer_confirmation_claim_id")).Should().BeFalse();

            await migrator.MigrateAsync();
            (await ColumnExistsAsync(
                dbContext,
                "transfer_confirmation_claim_id")).Should().BeTrue();
            (await ColumnExistsAsync(
                dbContext,
                "transfer_confirmation_claimed_at")).Should().BeTrue();
            (await ColumnExistsAsync(
                dbContext,
                "transfer_confirmation_claimed_by_user_id")).Should().BeTrue();
            (await IndexExistsAsync(
                dbContext,
                "idx_parcels_transfer_confirmation_claimed_at"))
                .Should().BeTrue();

            await migrator.MigrateAsync(PreviousMigration);
            (await ColumnExistsAsync(
                dbContext,
                "transfer_confirmation_claim_id")).Should().BeFalse();
            (await IndexExistsAsync(
                dbContext,
                "idx_parcels_transfer_confirmation_claimed_at"))
                .Should().BeFalse();

            await migrator.MigrateAsync();
            (await ColumnExistsAsync(
                dbContext,
                "transfer_confirmation_claim_id")).Should().BeTrue();
            (await IndexExistsAsync(
                dbContext,
                "idx_parcels_transfer_confirmation_claimed_at"))
                .Should().BeTrue();
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ClaimAndTimeoutCas_EnforceStrictDeadlineAndSingleCompletion()
    {
        var databaseName = $"vietride_parcel_day35_claim_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var claimable = CreateParcel("VRP-DAY35-CLAIM");
            var expired = CreateParcel("VRP-DAY35-EXPIRED");
            var claimableTargetTripId = Guid.NewGuid();
            var expiredTargetTripId = Guid.NewGuid();

            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.AddRange(claimable, expired);
                await seedContext.SaveChangesAsync();
                await SetPendingTransferAsync(
                    seedContext,
                    claimable.Id,
                    claimableTargetTripId,
                    Now.AddMinutes(-29));
                await SetPendingTransferAsync(
                    seedContext,
                    expired.Id,
                    expiredTargetTripId,
                    Now.AddMinutes(-30));
            }

            var firstClaimId = Guid.NewGuid();
            var secondClaimId = Guid.NewGuid();
            var firstCrewUserId = Guid.NewGuid();
            var secondCrewUserId = Guid.NewGuid();
            ParcelTransferConfirmationSnapshot?[] claimResults;
            await using (var firstContext = CreateDbContext(dataSource))
            await using (var secondContext = CreateDbContext(dataSource))
            {
                var first = CreateRepository(firstContext)
                    .TryClaimTransferConfirmationAsync(
                        claimable.Id,
                        claimable.ParcelCode,
                        claimable.TripId,
                        claimableTargetTripId,
                        firstClaimId,
                        firstCrewUserId,
                        Now,
                        CancellationToken.None);
                var second = CreateRepository(secondContext)
                    .TryClaimTransferConfirmationAsync(
                        claimable.Id,
                        claimable.ParcelCode,
                        claimable.TripId,
                        claimableTargetTripId,
                        secondClaimId,
                        secondCrewUserId,
                        Now,
                        CancellationToken.None);
                claimResults = await Task.WhenAll(first, second);
            }

            claimResults.Count(result => result is not null).Should().Be(1);
            var winningClaim = claimResults.Single(result => result is not null)!;

            await using (var expiredClaimContext = CreateDbContext(dataSource))
            {
                var claim = await CreateRepository(expiredClaimContext)
                    .TryClaimTransferConfirmationAsync(
                        expired.Id,
                        expired.ParcelCode,
                        expired.TripId,
                        expiredTargetTripId,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        Now,
                        CancellationToken.None);
                claim.Should().BeNull(
                    "claim requires now < requestedAt + 30 minutes");
            }

            await using (var timeoutContext = CreateDbContext(dataSource))
            {
                var escalated = await CreateRepository(timeoutContext)
                    .TryBulkEscalatePendingTransfersAsync(
                        Now.AddMinutes(1),
                        Now.AddMinutes(31),
                        10,
                        CancellationToken.None);
                escalated.Should().NotContain(item =>
                    item.ParcelId == claimable.Id);
                escalated.Should().ContainSingle(item =>
                    item.ParcelId == expired.Id
                    && item.Status == ParcelStatus.TRANSFER_ESCALATED);
            }

            ParcelTransferConfirmationSnapshot?[] completionResults;
            await using (var firstContext = CreateDbContext(dataSource))
            await using (var secondContext = CreateDbContext(dataSource))
            {
                var first = CreateRepository(firstContext)
                    .TryCompleteTransferConfirmationAsync(
                        claimable.Id,
                        claimable.TripId,
                        claimableTargetTripId,
                        winningClaim.ClaimId!.Value,
                        winningClaim.ClaimedByUserId!.Value,
                        Now.AddMinutes(31),
                        CancellationToken.None);
                var second = CreateRepository(secondContext)
                    .TryCompleteTransferConfirmationAsync(
                        claimable.Id,
                        claimable.TripId,
                        claimableTargetTripId,
                        winningClaim.ClaimId.Value,
                        winningClaim.ClaimedByUserId.Value,
                        Now.AddMinutes(31),
                        CancellationToken.None);
                completionResults = await Task.WhenAll(first, second);
            }

            completionResults.Count(result => result is not null)
                .Should().Be(1);
            completionResults.Single(result => result is not null)!.Status
                .Should().Be(ParcelStatus.LOADED);

        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Theory]
    [InlineData(ParcelStatus.PENDING_OPERATOR_REVIEW)]
    [InlineData(ParcelStatus.PENDING_PAYMENT)]
    public async Task ManualCancelRepository_EarlyPreLoadStatus_BecomesCancelled(
        ParcelStatus sourceStatus)
    {
        var databaseName =
            $"vietride_parcel_day32_manual_cancel_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var parcelCode = sourceStatus == ParcelStatus.PENDING_OPERATOR_REVIEW
                ? "VRP-D32-REVIEW"
                : "VRP-D32-PAYMENT";
            var parcel = CreateParcel(parcelCode);
            await using var dbContext = CreateDbContext(dataSource);
            await dbContext.Database.MigrateAsync();
            dbContext.Parcels.Add(parcel);
            await dbContext.SaveChangesAsync();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET status = CAST({sourceStatus.ToString()}
                    AS vietride_parcel.parcel_status)
                WHERE id = {parcel.Id};
                """);

            var result = await CreateRepository(dbContext)
                .TryManualCancelAsync(
                    parcel.Id,
                    parcel.OperatorId,
                    ParcelStatus.CANCELLED,
                    "operator cancelled",
                    75_000,
                    Now,
                    CancellationToken.None);

            result.Should().NotBeNull();
            result!.Status.Should().Be(ParcelStatus.CANCELLED);
            dbContext.ChangeTracker.Clear();
            var persisted = await dbContext.Parcels
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == parcel.Id);
            persisted.Status.Should().Be(ParcelStatus.CANCELLED);
            persisted.CancellationReason.Should().Be("operator cancelled");
            persisted.RejectionReason.Should().BeNull();
            persisted.RejectedAt.Should().BeNull();
            persisted.RefundDueVnd.Amount.Should().Be(75_000);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ParcelEntity CreateParcel(string parcelCode)
        => ParcelEntity.CreatePendingPayment(
            parcelCode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
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

    private static Task SetPendingTransferAsync(
        ParcelDbContext dbContext,
        Guid parcelId,
        Guid targetTripId,
        DateTimeOffset requestedAt)
        => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = 'PENDING_TRANSFER_CONFIRM'::vietride_parcel.parcel_status,
                transfer_target_trip_id = {targetTripId},
                transfer_requested_at = {requestedAt}
            WHERE id = {parcelId};
            """);

    private static async Task<bool> ColumnExistsAsync(
        ParcelDbContext dbContext,
        string columnName)
    {
        await using var command = dbContext.Database.GetDbConnection()
            .CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'vietride_parcel'
                  AND table_name = 'parcels'
                  AND column_name = @column_name);
            """;
        command.Parameters.Add(new NpgsqlParameter<string>(
            "column_name",
            columnName));
        await EnsureOpenAsync(command.Connection!);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> IndexExistsAsync(
        ParcelDbContext dbContext,
        string indexName)
    {
        await using var command = dbContext.Database.GetDbConnection()
            .CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = 'vietride_parcel'
                  AND tablename = 'parcels'
                  AND indexname = @index_name);
            """;
        command.Parameters.Add(new NpgsqlParameter<string>(
            "index_name",
            indexName));
        await EnsureOpenAsync(command.Connection!);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static IParcelRepository CreateRepository(
        ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;
        return (IParcelRepository)Activator.CreateInstance(
            repositoryType,
            dbContext)!;
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(
        NpgsqlDataSource dataSource)
    {
        var options = ParcelIntegrationDbContextOptions.Create(dataSource);
        return new ParcelDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configuredConnectionString =
            Environment.GetEnvironmentVariable(
                "VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(
            configuredConnectionString)
            ? defaultConnectionString
            : configuredConnectionString;
        connectionString = connectionString.Replace(
            "{databaseName}",
            databaseName,
            StringComparison.OrdinalIgnoreCase);
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(
        string connectionString,
        string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(
            connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(
            adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName}\";",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(
        string connectionString,
        string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(
            connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(
            adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureOpenAsync(
        System.Data.Common.DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }
}
