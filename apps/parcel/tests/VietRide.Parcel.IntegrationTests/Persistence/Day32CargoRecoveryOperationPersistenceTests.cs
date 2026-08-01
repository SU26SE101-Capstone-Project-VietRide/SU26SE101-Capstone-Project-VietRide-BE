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

public sealed class Day32CargoRecoveryOperationPersistenceTests
{
    private const string PreviousMigration =
        "20260730041619_AddParcelTransferConfirmationClaim";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_UpDownAndReapply_CreatesDurableRecoveryTable()
    {
        var databaseName = $"vietride_parcel_day32_operation_migration_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var dbContext = CreateDbContext(dataSource);
            var migrator = dbContext.GetService<IMigrator>();

            await migrator.MigrateAsync(PreviousMigration);
            (await TableExistsAsync(dbContext)).Should().BeFalse();

            await migrator.MigrateAsync();
            (await TableExistsAsync(dbContext)).Should().BeTrue();
            (await IndexExistsAsync(
                dbContext,
                "uq_parcel_cargo_recovery_operations_active_parcel"))
                .Should().BeTrue();
            (await IndexExistsAsync(
                dbContext,
                "idx_parcel_cargo_recovery_operations_stale"))
                .Should().BeTrue();

            await migrator.MigrateAsync(PreviousMigration);
            (await TableExistsAsync(dbContext)).Should().BeFalse();

            await migrator.MigrateAsync();
            (await TableExistsAsync(dbContext)).Should().BeTrue();
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ConcurrentTransferAndReturnClaims_AllowExactlyOnePendingOperation()
    {
        var databaseName = $"vietride_parcel_day32_operation_race_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var parcel = CreateParcel("VRP-D32-OP-RACE");
            var targetTripId = Guid.NewGuid();
            await SeedPendingOperatorActionAsync(
                dataSource,
                parcel,
                depositPaidVnd: 150_000,
                balancePaidVnd: 50_000,
                refundedAmountVnd: 25_000);

            ParcelCargoRecoveryOperationSnapshot?[] results;
            await using (var transferContext = CreateDbContext(dataSource))
            await using (var returnContext = CreateDbContext(dataSource))
            {
                var transfer = CreateRepository(transferContext)
                    .TryClaimCargoRecoveryTransferAsync(
                        Guid.NewGuid(),
                        parcel.Id,
                        parcel.OperatorId,
                        targetTripId,
                        Guid.NewGuid(),
                        "transfer",
                        Now,
                        CancellationToken.None);
                var returned = CreateRepository(returnContext)
                    .TryClaimCargoRecoveryReturnAsync(
                        Guid.NewGuid(),
                        parcel.Id,
                        parcel.OperatorId,
                        Guid.NewGuid(),
                        "return",
                        false,
                        Now,
                        CancellationToken.None);
                results = await Task.WhenAll(transfer, returned);
            }

            results.Count(result => result is not null).Should().Be(1);
            await using var assertContext = CreateDbContext(dataSource);
            (await assertContext.ParcelCargoRecoveryOperations.CountAsync(operation =>
                operation.ParcelId == parcel.Id
                && operation.Status == ParcelCargoRecoveryOperationStatus.PENDING))
                .Should().Be(1);
            var unchanged = await assertContext.Parcels
                .AsNoTracking()
                .SingleAsync(item => item.Id == parcel.Id);
            unchanged.Status.Should().Be(ParcelStatus.PENDING_OPERATOR_ACTION);
            unchanged.TripId.Should().Be(parcel.TripId);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task PersistedTransferCrashReplay_CompletesExactlyOnce()
    {
        var databaseName = $"vietride_parcel_day32_operation_replay_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var parcel = CreateParcel("VRP-D32-OP-REPLAY");
            var targetTripId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            await SeedPendingOperatorActionAsync(dataSource, parcel, 0, 0, 0);

            await using (var claimContext = CreateDbContext(dataSource))
            {
                var claimed = await CreateRepository(claimContext)
                    .TryClaimCargoRecoveryTransferAsync(
                        operationId,
                        parcel.Id,
                        parcel.OperatorId,
                        targetTripId,
                        Guid.NewGuid(),
                        "transfer after cancellation",
                        Now,
                        CancellationToken.None);
                claimed.Should().NotBeNull();
            }

            ParcelPaymentTransitionSnapshot?[] completions;
            await using (var firstContext = CreateDbContext(dataSource))
            await using (var secondContext = CreateDbContext(dataSource))
            {
                var first = CreateRepository(firstContext)
                    .TryCompleteCargoRecoveryTransferAsync(
                        operationId,
                        Now.AddMinutes(6),
                        CancellationToken.None);
                var second = CreateRepository(secondContext)
                    .TryCompleteCargoRecoveryTransferAsync(
                        operationId,
                        Now.AddMinutes(6),
                        CancellationToken.None);
                completions = await Task.WhenAll(first, second);
            }

            completions.Count(result => result is not null).Should().Be(1);
            await using var assertContext = CreateDbContext(dataSource);
            var completedOperation = await assertContext.ParcelCargoRecoveryOperations
                .AsNoTracking()
                .SingleAsync(operation => operation.Id == operationId);
            completedOperation.Status.Should()
                .Be(ParcelCargoRecoveryOperationStatus.COMPLETED);
            var recoveredParcel = await assertContext.Parcels
                .AsNoTracking()
                .SingleAsync(item => item.Id == parcel.Id);
            recoveredParcel.Status.Should().Be(ParcelStatus.RESERVED);
            recoveredParcel.TripId.Should().Be(targetTripId);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ReturnClaim_FreezesOutstandingRefundFromAuthoritativeRow()
    {
        var databaseName = $"vietride_parcel_day32_operation_refund_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var parcel = CreateParcel("VRP-D32-OP-REFUND");
            await SeedPendingOperatorActionAsync(
                dataSource,
                parcel,
                depositPaidVnd: 150_000,
                balancePaidVnd: 50_000,
                refundedAmountVnd: 25_000);

            await using var dbContext = CreateDbContext(dataSource);
            var claimed = await CreateRepository(dbContext)
                .TryClaimCargoRecoveryReturnAsync(
                    Guid.NewGuid(),
                    parcel.Id,
                    parcel.OperatorId,
                    Guid.NewGuid(),
                    "return to sender",
                    false,
                    Now,
                    CancellationToken.None);

            claimed.Should().NotBeNull();
            claimed!.RefundAmountVnd.Should().Be(175_000);
            claimed.RefundDueVnd.Should().Be(200_000);
            claimed.SourceStatus.Should().Be(ParcelStatus.PENDING_OPERATOR_ACTION);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task SeedPendingOperatorActionAsync(
        NpgsqlDataSource dataSource,
        ParcelEntity parcel,
        long depositPaidVnd,
        long balancePaidVnd,
        long refundedAmountVnd)
    {
        await using var dbContext = CreateDbContext(dataSource);
        await dbContext.Database.MigrateAsync();
        dbContext.Parcels.Add(parcel);
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = 'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status,
                deposit_paid_vnd = {depositPaidVnd},
                balance_paid_vnd = {balancePaidVnd},
                refunded_amount_vnd = {refundedAmountVnd},
                refund_due_vnd = {refundedAmountVnd}
            WHERE id = {parcel.Id};
            """);
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
            Money.FromRaw(200_000));

    private static async Task<bool> TableExistsAsync(ParcelDbContext dbContext)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT to_regclass(
                'vietride_parcel.parcel_cargo_recovery_operations') IS NOT NULL;
            """;
        await EnsureOpenAsync(command.Connection!);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> IndexExistsAsync(
        ParcelDbContext dbContext,
        string indexName)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = 'vietride_parcel'
                  AND tablename = 'parcel_cargo_recovery_operations'
                  AND indexname = @index_name);
            """;
        command.Parameters.Add(new NpgsqlParameter<string>("index_name", indexName));
        await EnsureOpenAsync(command.Connection!);
        return (bool)(await command.ExecuteScalarAsync())!;
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
        const string defaultConnectionString =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configuredConnectionString = Environment.GetEnvironmentVariable(
            "VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
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
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
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
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
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
