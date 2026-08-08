using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.Maintenance;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Payment.IntegrationTests.RevenueAnalytics;

public sealed class RevenueAnalyticsRepositoryParcelVoucherBackfillTests
{
    private const string ScratchPrefix = "vietride_revenue_backfill_";

    [Fact]
    public async Task DryRunApplyAndRerun_AreSafeAndIdempotent()
    {
        var databaseName = $"{ScratchPrefix}{Guid.NewGuid():N}";
        await using var db = CreateDbContext(CreateConnectionString(databaseName));
        try
        {
            await db.Database.MigrateAsync();
            var operatorId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var parcelId = Guid.NewGuid();
            var refundSourceId = Guid.NewGuid();
            db.OperatorLedgerEntries.AddRange(
                OperatorLedgerEntry.Create(
                    operatorId,
                    tripId,
                    OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT,
                    30_000,
                    OperatorLedgerReferenceType.PARCEL,
                    parcelId,
                    Guid.NewGuid()),
                OperatorLedgerEntry.Create(
                    operatorId,
                    tripId,
                    OperatorLedgerEntryType.PARCEL_REFUND,
                    -75_000,
                    OperatorLedgerReferenceType.PARCEL,
                    parcelId,
                    refundSourceId));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_payment.operator_ledger_entries
                    (operator_id, trip_id, entry_type, adjustment_reason, amount,
                     reference_type, reference_id, source_event_id, note)
                VALUES
                    ({operatorId}, NULL, 'ADJUSTMENT', 'LEGACY_UNCLASSIFIED', 1,
                     'MANUAL', {Guid.NewGuid()}, {Guid.NewGuid()}, 'legacy')
                """);
            db.ChangeTracker.Clear();
            var service = new ParcelVoucherReversalBackfillService(db);

            var dryRun = await service.ExecuteAsync(true, CancellationToken.None);

            dryRun.ScannedRefundCount.Should().Be(1);
            dryRun.CandidateCount.Should().Be(1);
            dryRun.SkippedExistingCount.Should().Be(0);
            dryRun.LegacyUnclassifiedCount.Should().Be(1);
            dryRun.TotalAdjustmentVnd.Should().Be(-30_000);
            dryRun.AppliedCount.Should().Be(0);
            (await CountReversalsAsync(db)).Should().Be(0);

            var applied = await service.ExecuteAsync(false, CancellationToken.None);

            applied.CandidateCount.Should().Be(1);
            applied.AppliedCount.Should().Be(1);
            (await CountReversalsAsync(db)).Should().Be(1);
            var reversal = await db.OperatorLedgerEntries.AsNoTracking().SingleAsync(entry =>
                entry.AdjustmentReason == OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL);
            reversal.SourceEventId.Should().Be(
                RevenueLedgerWriter.CreateParcelVoucherAdjustmentSourceId(refundSourceId, parcelId));

            var rerun = await service.ExecuteAsync(false, CancellationToken.None);

            rerun.CandidateCount.Should().Be(0);
            rerun.SkippedExistingCount.Should().Be(1);
            rerun.TotalAdjustmentVnd.Should().Be(0);
            rerun.AppliedCount.Should().Be(0);
            (await CountReversalsAsync(db)).Should().Be(1);
        }
        finally
        {
            await DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static Task<int> CountReversalsAsync(PaymentDbContext db)
        => db.OperatorLedgerEntries.AsNoTracking().CountAsync(entry =>
            entry.AdjustmentReason == OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL);

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
