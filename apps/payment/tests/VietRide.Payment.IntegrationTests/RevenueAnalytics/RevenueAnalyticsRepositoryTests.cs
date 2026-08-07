using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Payment.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.IntegrationTests.RevenueAnalytics;

public sealed class RevenueAnalyticsRepositoryTests
{
    private const string ScratchPrefix = "vietride_ui20_revenue_";

    [Fact]
    public async Task PostgreSqlCoreUsesCanonicalSourcesClassificationIctBoundariesAndOneSqlPerRead()
    {
        var databaseName = $"{ScratchPrefix}{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-07-15T00:00:00Z"));
        await using var setupDb = CreateDbContext(connectionString, clock);
        try
        {
            await setupDb.Database.MigrateAsync();
            var seed = await SeedAsync(setupDb, clock);
            var interceptor = new CountingCommandInterceptor();
            await using var queryDb = CreateDbContext(connectionString, clock, interceptor);
            var repository = CreateRepository(queryDb);
            var fromUtc = DateTimeOffset.Parse("2026-06-30T17:00:00Z");
            var toUtc = DateTimeOffset.Parse("2026-07-31T17:00:00Z");

            var monthly = await repository.GetAdminMonthlyRevenueAsync(fromUtc, toUtc);

            monthly.Should().ContainSingle().Which.Should().Be(
                new AdminRevenueMonthReadModel(new DateOnly(2026, 7, 1), 700, 400));
            interceptor.ReaderCount.Should().Be(1);

            interceptor.Reset();
            var top = await repository.GetTopOperatorPayoutsAsync(fromUtc, toUtc, 20);

            top.Should().Equal(
                new TopOperatorPayoutReadModel(seed.OperatorId, 300),
                new TopOperatorPayoutReadModel(seed.SecondOperatorId, 100));
            interceptor.ReaderCount.Should().Be(1);

            interceptor.Reset();
            var ledger = await repository.GetOperatorRevenueLedgerAsync(
                seed.OperatorId,
                fromUtc,
                toUtc);

            ledger.Should().ContainSingle().Which.Should().Be(
                new OperatorRevenueLedgerReadModel(
                    new DateOnly(2026, 7, 1),
                    seed.TripId,
                    85,
                    43,
                    1,
                    1));
            interceptor.ReaderCount.Should().Be(1);

            var overflowOperatorId = Guid.NewGuid();
            var overflowTripId = Guid.NewGuid();
            clock.UtcNow = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
            setupDb.OperatorLedgerEntries.AddRange(
                Ledger(overflowOperatorId, overflowTripId, OperatorLedgerEntryType.BOOKING_REVENUE, long.MaxValue, OperatorLedgerReferenceType.BOOKING, Guid.NewGuid()),
                Ledger(overflowOperatorId, overflowTripId, OperatorLedgerEntryType.BOOKING_REVENUE, long.MaxValue, OperatorLedgerReferenceType.BOOKING, Guid.NewGuid()));
            await setupDb.SaveChangesAsync();

            interceptor.Reset();
            var overflow = () => repository.GetOperatorRevenueLedgerAsync(
                overflowOperatorId,
                fromUtc,
                toUtc);

            var exception = await overflow.Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be(PostgresErrorCodes.NumericValueOutOfRange);
            interceptor.ReaderCount.Should().Be(1);
        }
        finally
        {
            var connectedDatabase = setupDb.Database.GetDbConnection().Database;
            if (!databaseName.StartsWith(ScratchPrefix, StringComparison.Ordinal)
                || !string.Equals(connectedDatabase, databaseName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Refusing to delete non-scratch database '{connectedDatabase}'.");
            }

            await setupDb.Database.EnsureDeletedAsync();
        }
    }

    private static IRevenueAnalyticsRepository CreateRepository(PaymentDbContext dbContext)
    {
        var type = typeof(PaymentDbContext).Assembly.GetType(
            "VietRide.Payment.Infrastructure.Persistence.Repositories.RevenueAnalyticsRepository",
            throwOnError: true)!;
        return (IRevenueAnalyticsRepository)Activator.CreateInstance(type, dbContext)!;
    }

    private static PaymentDbContext CreateDbContext(
        string connectionString,
        IClock clock,
        DbCommandInterceptor? interceptor = null)
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
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentDbContext.SchemaName));
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new PaymentDbContext(options.Options, clock);
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_PAYMENT_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        var expanded = template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
        return new NpgsqlConnectionStringBuilder(expanded) { Database = databaseName }.ConnectionString;
    }

    private static async Task<Seed> SeedAsync(PaymentDbContext dbContext, MutableClock clock)
    {
        var operatorId = Guid.NewGuid();
        var secondOperatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var secondTripId = Guid.NewGuid();

        var atStart = DateTimeOffset.Parse("2026-06-30T17:00:00Z");
        var beforeStart = atStart.AddTicks(-1);
        var atEnd = DateTimeOffset.Parse("2026-07-31T17:00:00Z");
        var inside = DateTimeOffset.Parse("2026-07-15T03:00:00Z");
        var subscriptions = new[]
        {
            SucceededSubscription(operatorId, 500, atStart),
            SucceededSubscription(operatorId, 200, inside),
            SucceededSubscription(operatorId, 9_999, beforeStart),
            SucceededSubscription(operatorId, 9_999, atEnd),
        };
        var pendingSubscription = PaymentEntity.CreatePendingRedirectVnPaySubscription(
            Guid.NewGuid(),
            operatorId,
            Money.FromRaw(8_888),
            $"ui20-{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("D"),
            "https://payment.test/redirect",
            inside.AddMinutes(15));
        var bookingPayment = PaymentEntity.CreateSucceededWalletBookingCharge(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(7_777),
            inside);
        var settlementSeeds = new[]
        {
            PendingSettlement(operatorId, tripId, 300, atStart),
            PendingSettlement(secondOperatorId, secondTripId, 100, inside),
            PendingSettlement(operatorId, Guid.NewGuid(), 9_999, beforeStart),
            PendingSettlement(operatorId, Guid.NewGuid(), 9_999, atEnd),
        };
        var cancelledSettlement = OperatorTripSettlement.CreatePending(
            operatorId,
            Guid.NewGuid(),
            inside.AddDays(-8));
        cancelledSettlement.RefreshEligibility(0, inside);
        dbContext.AddRange(subscriptions);
        dbContext.AddRange(pendingSubscription, bookingPayment, cancelledSettlement);
        dbContext.AddRange(settlementSeeds.Select(item => item.Entity));
        await dbContext.SaveChangesAsync();
        foreach (var settlement in settlementSeeds)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_payment.operator_trip_settlements
                SET net_amount = {settlement.Amount},
                    status = 'SETTLED',
                    settlement_method = 'AUTO_WEEKLY',
                    settled_at = {settlement.SettledAt},
                    wallet_transaction_id = NULL
                WHERE id = {settlement.Entity.Id}
                """);
        }
        dbContext.ChangeTracker.Clear();

        clock.UtcNow = atStart;
        var bookingReference = Guid.NewGuid();
        var parcelReference = Guid.NewGuid();
        dbContext.OperatorLedgerEntries.AddRange(
            Ledger(operatorId, tripId, OperatorLedgerEntryType.BOOKING_REVENUE, 100, OperatorLedgerReferenceType.BOOKING, bookingReference),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.BOOKING_REFUND, -20, OperatorLedgerReferenceType.BOOKING, bookingReference),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT, 10, OperatorLedgerReferenceType.BOOKING, bookingReference),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.ADJUSTMENT, -5, OperatorLedgerReferenceType.BOOKING, bookingReference, "reverse-vietride-funded-voucher"),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.PARCEL_REVENUE, 50, OperatorLedgerReferenceType.PARCEL, parcelReference),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.PARCEL_REFUND, -10, OperatorLedgerReferenceType.PARCEL, parcelReference),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT, 5, OperatorLedgerReferenceType.PARCEL, parcelReference),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.ADJUSTMENT, -2, OperatorLedgerReferenceType.PARCEL, parcelReference, "reverse-vietride-funded-voucher"),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.ADJUSTMENT, 1_000, OperatorLedgerReferenceType.BOOKING, bookingReference, "other-adjustment"),
            Ledger(operatorId, null, OperatorLedgerEntryType.ADJUSTMENT, 1_000, OperatorLedgerReferenceType.MANUAL, Guid.NewGuid(), "manual"),
            Ledger(operatorId, tripId, OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT, 0, OperatorLedgerReferenceType.BOOKING, bookingReference));
        await dbContext.SaveChangesAsync();

        clock.UtcNow = atEnd;
        dbContext.OperatorLedgerEntries.Add(
            Ledger(operatorId, tripId, OperatorLedgerEntryType.BOOKING_REVENUE, 9_999, OperatorLedgerReferenceType.BOOKING, Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
        clock.UtcNow = inside;
        dbContext.OperatorLedgerEntries.Add(
            Ledger(secondOperatorId, secondTripId, OperatorLedgerEntryType.BOOKING_REVENUE, 9_999, OperatorLedgerReferenceType.BOOKING, Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return new Seed(operatorId, secondOperatorId, tripId);
    }

    private static PaymentEntity SucceededSubscription(Guid operatorId, long amount, DateTimeOffset succeededAt)
    {
        var payment = PaymentEntity.CreatePendingRedirectVnPaySubscription(
            Guid.NewGuid(),
            operatorId,
            Money.FromRaw(amount),
            $"ui20-{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("D"),
            "https://payment.test/redirect",
            succeededAt.AddMinutes(15));
        payment.MarkSucceeded("00", succeededAt);
        return payment;
    }

    private static SettlementSeed PendingSettlement(
        Guid operatorId,
        Guid tripId,
        long amount,
        DateTimeOffset settledAt)
    {
        var settlement = OperatorTripSettlement.CreatePending(operatorId, tripId, settledAt.AddDays(-8));
        return new SettlementSeed(settlement, amount, settledAt);
    }

    private static OperatorLedgerEntry Ledger(
        Guid operatorId,
        Guid? tripId,
        OperatorLedgerEntryType entryType,
        long amount,
        OperatorLedgerReferenceType referenceType,
        Guid referenceId,
        string? note = null)
    {
        if (entryType == OperatorLedgerEntryType.ADJUSTMENT
            && referenceType != OperatorLedgerReferenceType.MANUAL
            && note != "reverse-vietride-funded-voucher")
        {
            var legacy = OperatorLedgerEntry.Create(
                operatorId,
                null,
                entryType,
                amount,
                OperatorLedgerReferenceType.MANUAL,
                referenceId,
                Guid.NewGuid(),
                note,
                adjustmentReason: OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT);
            typeof(OperatorLedgerEntry).GetProperty(nameof(OperatorLedgerEntry.TripId))!
                .SetValue(legacy, tripId);
            typeof(OperatorLedgerEntry).GetProperty(nameof(OperatorLedgerEntry.ReferenceType))!
                .SetValue(legacy, referenceType);
            typeof(OperatorLedgerEntry).GetProperty(nameof(OperatorLedgerEntry.AdjustmentReason))!
                .SetValue(legacy, OperatorLedgerAdjustmentReason.LEGACY_UNCLASSIFIED);
            return legacy;
        }

        return OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            entryType,
            amount,
            referenceType,
            referenceId,
            Guid.NewGuid(),
            note,
            adjustmentReason: entryType == OperatorLedgerEntryType.ADJUSTMENT
                ? referenceType == OperatorLedgerReferenceType.MANUAL
                    ? OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT
                    : OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL
                : null);
    }

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

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed record Seed(Guid OperatorId, Guid SecondOperatorId, Guid TripId);

    private sealed record SettlementSeed(
        OperatorTripSettlement Entity,
        long Amount,
        DateTimeOffset SettledAt);
}
