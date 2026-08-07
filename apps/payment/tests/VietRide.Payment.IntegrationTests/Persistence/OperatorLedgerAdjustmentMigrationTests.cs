using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Payment.IntegrationTests.Persistence;

public sealed class OperatorLedgerAdjustmentMigrationTests
{
    private const string ScratchPrefix = "vietride_adjustment_migration_";
    private const string ExpandMigration = "20260807085712_AddOperatorLedgerAdjustmentReason";

    [Fact]
    public async Task ExpandClassifyEnforce_ClassifiesKnownAndLegacyRowsInOrder()
    {
        var databaseName = $"{ScratchPrefix}{Guid.NewGuid():N}";
        await using var db = CreateDbContext(CreateConnectionString(databaseName));
        try
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(ExpandMigration);
            var operatorId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            await InsertAdjustmentAsync(db, operatorId, tripId, -10, "BOOKING", "reverse-vietride-funded-voucher");
            await InsertAdjustmentAsync(db, operatorId, tripId, 0, "BOOKING", "generic-booking-refund-entitlement");
            await InsertAdjustmentAsync(db, operatorId, null, 10, "MANUAL", "admin correction");
            await InsertAdjustmentAsync(db, operatorId, tripId, 10, "BOOKING", "unknown legacy note");

            await migrator.MigrateAsync();

            var reasons = await db.Database.SqlQueryRaw<string>(
                    "SELECT adjustment_reason::text AS \"Value\" " +
                    "FROM vietride_payment.operator_ledger_entries ORDER BY note")
                .ToArrayAsync();
            reasons.Should().BeEquivalentTo(
                "VIETRIDE_FUNDED_VOUCHER_REVERSAL",
                "GENERIC_BOOKING_REFUND_ENTITLEMENT",
                "MANUAL_WALLET_ADJUSTMENT",
                "LEGACY_UNCLASSIFIED");

            await migrator.MigrateAsync(ExpandMigration);
            var nullReasonCount = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM vietride_payment.operator_ledger_entries " +
                    "WHERE adjustment_reason IS NULL")
                .SingleAsync();
            nullReasonCount.Should().Be(4);

            await migrator.MigrateAsync();
            var reappliedReasonCount = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM vietride_payment.operator_ledger_entries " +
                    "WHERE adjustment_reason IS NOT NULL")
                .SingleAsync();
            reappliedReasonCount.Should().Be(4);
        }
        finally
        {
            await DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static Task InsertAdjustmentAsync(
        PaymentDbContext db,
        Guid operatorId,
        Guid? tripId,
        long amount,
        string referenceType,
        string note)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_payment.operator_ledger_entries
                (operator_id, trip_id, entry_type, adjustment_reason, amount,
                 reference_type, reference_id, source_event_id, note)
            VALUES
                ({operatorId}, {tripId}, 'ADJUSTMENT', NULL, {amount},
                 {referenceType}::vietride_payment.operator_ledger_reference_type,
                 {Guid.NewGuid()}, {Guid.NewGuid()}, {note})
            """);

    private static PaymentDbContext CreateDbContext(string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<OutboxEventStatus>(
            $"{PaymentDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        PaymentDbContext.ConfigurePostgresTypes(dataSourceBuilder);
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(
                dataSourceBuilder.Build(),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentDbContext.SchemaName))
            .Options;
        return new PaymentDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_PAYMENT_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
            template = fallback;
        var expanded = template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
        return new NpgsqlConnectionStringBuilder(expanded) { Database = databaseName }.ConnectionString;
    }

    private static async Task DeleteScratchDatabaseAsync(PaymentDbContext db, string expectedDatabase)
    {
        var connectedDatabase = db.Database.GetDbConnection().Database;
        if (!expectedDatabase.StartsWith(ScratchPrefix, StringComparison.Ordinal)
            || !string.Equals(connectedDatabase, expectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to delete non-scratch database '{connectedDatabase}'.");
        }
        await db.Database.EnsureDeletedAsync();
    }
}
