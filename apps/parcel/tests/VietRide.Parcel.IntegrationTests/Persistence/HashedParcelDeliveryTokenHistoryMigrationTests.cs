using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class HashedParcelDeliveryTokenHistoryMigrationTests
{
    private const string PreviousMigration = "20260728094014_AddParcelEvidencePhotos";

    [Fact]
    public async Task UpBackfillsHashAndDownRestoresOnlyAnInvalidatedReplacementToken()
    {
        var databaseName = $"vietride_parcel_delivery_token_migration_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var dbContext = CreateDbContext(dataSource);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            var parcel = CreateParcel();
            var rawToken = Guid.Parse("11111111-2222-4333-8444-555555555555");
            var expiresAt = DateTimeOffset.UtcNow.AddHours(12);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_parcel.parcels
                    (id, parcel_code, sender_user_id, recipient_user_id,
                     recipient_name, recipient_phone, recipient_email,
                     operator_id, trip_id, description,
                     size_category, estimated_size_category,
                     estimated_weight_kg, delivery_method,
                     deposit_amount, original_deposit_amount,
                     total_price_vnd, estimated_total_price_vnd,
                     deposit_required_vnd, status,
                     delivery_token, delivery_token_expires_at,
                     delivery_token_revoked_at, created_at, updated_at)
                VALUES
                    ({parcel.Id}, {parcel.ParcelCode}, {parcel.SenderUserId},
                     {parcel.RecipientUserId}, {parcel.RecipientName},
                     {parcel.RecipientPhone.Value}, {parcel.RecipientEmail},
                     {parcel.OperatorId}, {parcel.TripId}, {parcel.Description},
                     CAST({parcel.SizeCategory.ToString()}
                         AS vietride_parcel.parcel_size_category),
                     CAST({parcel.SizeCategory.ToString()}
                         AS vietride_parcel.parcel_size_category),
                     {parcel.EstimatedWeightKg},
                     CAST({parcel.DeliveryMethod.ToString()}
                         AS vietride_parcel.parcel_delivery_method),
                     {parcel.DepositAmount.Amount},
                     {parcel.OriginalDepositAmount.Amount},
                     {parcel.TotalPrice.Amount},
                     {parcel.EstimatedTotalPriceVnd.Amount},
                     {parcel.DepositRequiredVnd.Amount},
                     CAST({parcel.Status.ToString()}
                         AS vietride_parcel.parcel_status),
                     {rawToken}, {expiresAt}, NULL, now(), now());
                """);

            await migrator.MigrateAsync();

            (await ColumnExistsAsync(
                dbContext,
                "parcels",
                "delivery_token")).Should().BeFalse();
            var migrated = await ReadTokenHistoryAsync(dbContext, parcel.Id);
            migrated.TokenHash.Should().Be(DeliveryTokenHasher.Hash(rawToken));
            migrated.ExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromMilliseconds(1));
            migrated.RevokedAt.Should().BeNull();
            migrated.IssueReason.Should().Be(ParcelDeliveryTokenIssueReason.MIGRATION_BACKFILL.ToString());

            var duplicateHash = "a" + new string('0', 63);
            const string duplicateReason = "RESEND";
            var duplicateActiveInsert = async () =>
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO vietride_parcel.parcel_delivery_tokens
                        (id, parcel_id, token_hash, expires_at, revoked_at,
                         issued_by_user_id, issue_reason, created_at, updated_at)
                    VALUES
                        ({Guid.NewGuid()}, {parcel.Id},
                         {duplicateHash}, {expiresAt}, NULL,
                         NULL, {duplicateReason}, now(), now());
                    """);
            var uniqueViolation = await duplicateActiveInsert.Should()
                .ThrowAsync<PostgresException>();
            uniqueViolation.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

            await migrator.MigrateAsync(PreviousMigration);

            (await TableExistsAsync(
                dbContext,
                "parcel_delivery_tokens")).Should().BeFalse();
            var restored = await ReadLegacyTokenAsync(dbContext, parcel.Id);
            restored.RawToken.Should().NotBe(rawToken);
            restored.ExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromMilliseconds(1));
            restored.RevokedAt.Should().NotBeNull(
                "Down() must never fabricate a usable plaintext token from an irreversible hash");

            await migrator.MigrateAsync();
            var remigrated = await ReadTokenHistoryAsync(dbContext, parcel.Id);
            remigrated.RevokedAt.Should().NotBeNull();
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            "VRP-TOKEN-MIGRATION-001",
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

    private static async Task<(
        string TokenHash,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? RevokedAt,
        string IssueReason)> ReadTokenHistoryAsync(
        ParcelDbContext dbContext,
        Guid parcelId)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT token_hash, expires_at, revoked_at, issue_reason
            FROM vietride_parcel.parcel_delivery_tokens
            WHERE parcel_id = @parcel_id;
            """;
        command.Parameters.Add(new NpgsqlParameter<Guid>("parcel_id", parcelId));
        await EnsureOpenAsync(command.Connection!);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (
            reader.GetString(0).TrimEnd(),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetString(3));
    }

    private static async Task<(
        Guid? RawToken,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? RevokedAt)> ReadLegacyTokenAsync(
        ParcelDbContext dbContext,
        Guid parcelId)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT delivery_token, delivery_token_expires_at, delivery_token_revoked_at
            FROM vietride_parcel.parcels
            WHERE id = @parcel_id;
            """;
        command.Parameters.Add(new NpgsqlParameter<Guid>("parcel_id", parcelId));
        await EnsureOpenAsync(command.Connection!);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private static async Task<bool> ColumnExistsAsync(
        ParcelDbContext dbContext,
        string tableName,
        string columnName)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'vietride_parcel'
                  AND table_name = @table_name
                  AND column_name = @column_name);
            """;
        command.Parameters.Add(new NpgsqlParameter<string>("table_name", tableName));
        command.Parameters.Add(new NpgsqlParameter<string>("column_name", columnName));
        await EnsureOpenAsync(command.Connection!);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> TableExistsAsync(
        ParcelDbContext dbContext,
        string tableName)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'vietride_parcel'
                  AND table_name = @table_name);
            """;
        command.Parameters.Add(new NpgsqlParameter<string>("table_name", tableName));
        await EnsureOpenAsync(command.Connection!);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task EnsureOpenAsync(System.Data.Common.DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<ParcelDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    ParcelDbContext.SchemaName))
            .Options;

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

        return new NpgsqlConnectionStringBuilder(
            connectionString.Replace(
                "{databaseName}",
                databaseName,
                StringComparison.OrdinalIgnoreCase))
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
}
