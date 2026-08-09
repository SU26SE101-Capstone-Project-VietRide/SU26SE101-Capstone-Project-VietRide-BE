using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Payment.IntegrationTests.Persistence;

public sealed class OperatorFinancialTransparencyPostgresTests
{
    private const string ScratchPrefix = "vietride_wallet_transparency_";

    [Fact]
    public async Task OperatorFinancialProjection_ReconcilesLifecycleSearchMetadataAndSafeFailure()
    {
        var databaseName = $"{ScratchPrefix}{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        var now = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(now);
        var operatorId = Guid.NewGuid();
        var otherOperatorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var awaitingTripId = Guid.NewGuid();
        var legacyTripId = Guid.NewGuid();
        var pendingTripId = Guid.NewGuid();
        var eligibleTripId = Guid.NewGuid();
        var settledTripId = Guid.NewGuid();
        var trips = new FakeTripClient(
        [
            Summary(pendingTripId, "Pending route"),
            Summary(legacyTripId, "Legacy route"),
            Summary(eligibleTripId, "Eligible route"),
            Summary(settledTripId, "Settled route"),
        ]);

        await using var provider = CreateProvider(connectionString, clock, trips);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        try
        {
            await db.Database.MigrateAsync();

            var wallet = OperatorWallet.Create(operatorId);
            wallet.Credit(Money.FromRaw(450));
            db.OperatorWallets.Add(wallet);

            var pending = OperatorTripSettlement.CreatePending(operatorId, pendingTripId, now.AddDays(-1));
            pending.RefreshEligibility(999, now);
            var legacySettlement = OperatorTripSettlement.CreatePending(operatorId, legacyTripId, now.AddDays(-1));
            legacySettlement.RefreshEligibility(999, now);
            var eligible = OperatorTripSettlement.CreatePending(operatorId, eligibleTripId, now.AddDays(-8));
            eligible.RefreshEligibility(999, now);
            eligible.RecordFailure("PLATFORM_WALLET_INSUFFICIENT_BALANCE", now.AddDays(-7));
            var settled = OperatorTripSettlement.CreatePending(operatorId, settledTripId, now.AddDays(-9));
            settled.RefreshEligibility(400, now);
            var settlementMovement = OperatorWalletTransaction.Create(
                operatorId,
                OperatorWalletTransactionType.CREDIT,
                Money.FromRaw(400),
                Money.FromRaw(0),
                Money.FromRaw(400),
                OperatorWalletTransactionRef.TRIP_SETTLEMENT,
                settled.Id,
                "Trip settlement");
            settled.MarkSettled(
                400,
                OperatorTripSettlementMethod.AUTO_WEEKLY,
                now.AddDays(-1),
                null,
                settlementMovement.Id);
            db.OperatorTripSettlements.AddRange(pending, legacySettlement, eligible, settled);
            db.OperatorWalletTransactions.Add(settlementMovement);

            var adjustmentMovement = OperatorWalletTransaction.Create(
                operatorId,
                OperatorWalletTransactionType.CREDIT,
                Money.FromRaw(50),
                Money.FromRaw(400),
                Money.FromRaw(450),
                OperatorWalletTransactionRef.ADJUSTMENT,
                null,
                "Manual correction");
            db.OperatorWalletTransactions.Add(adjustmentMovement);
            db.OperatorLedgerEntries.Add(OperatorLedgerEntry.Create(
                operatorId,
                null,
                OperatorLedgerEntryType.ADJUSTMENT,
                50,
                OperatorLedgerReferenceType.MANUAL,
                adjustmentMovement.Id,
                adjustmentMovement.Id,
                "Manual correction",
                new FinancialActorSnapshot(adminId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN"),
                OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT,
                occurredAt: now));

            db.OperatorLedgerEntries.AddRange(
                Revenue(operatorId, awaitingTripId, 100, "VR-AWAITING", now.AddHours(-5)),
                LegacyRevenue(operatorId, legacyTripId, 25),
                Revenue(operatorId, pendingTripId, 200, "VR-PENDING", now.AddHours(-4)),
                Refund(operatorId, pendingTripId, -20, "VR-PENDING", now.AddHours(-3)),
                Revenue(operatorId, eligibleTripId, 300, "VR-ELIGIBLE", now.AddHours(-6), "fee 100%_done\\ok"),
                VietRideVoucher(operatorId, eligibleTripId, 30, "VR-ELIGIBLE", now.AddHours(-6)),
                OperatorVoucher(operatorId, eligibleTripId, 20, "VR-ELIGIBLE", now.AddHours(-6)),
                Refund(operatorId, eligibleTripId, -50, "VR-ELIGIBLE", now.AddHours(-2)),
                VoucherReversal(operatorId, eligibleTripId, -30, "VR-ELIGIBLE", now.AddHours(-2)),
                Revenue(operatorId, settledTripId, 400, "VR-SETTLED", now.AddDays(-2)),
                Revenue(otherOperatorId, Guid.NewGuid(), 9_999, "VR-ELIGIBLE", now, "fee 100%_done\\ok"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var service = scope.ServiceProvider.GetRequiredService<IFinancialManagementService>();
            var summary = await service.GetOperatorWalletAsync(operatorId, CancellationToken.None);

            summary.Balance.Should().Be(450);
            summary.Currency.Should().Be("VND");
            summary.AwaitingTripCompletionAmount.Should().Be(100);
            summary.AwaitingTripCompletionCount.Should().Be(1);
            summary.PendingHoldAmount.Should().Be(205);
            summary.PendingHoldCount.Should().Be(2);
            summary.EligibleAmount.Should().Be(250);
            summary.EligibleCount.Should().Be(1);
            summary.LifetimeSettledAmount.Should().Be(400);
            summary.LastSettlement.Should().NotBeNull();
            summary.WithdrawalSupported.Should().BeFalse();
            summary.CalculatedAt.Should().Be(now);

            var settlements = await service.ListOperatorSettlementsAsync(
                operatorId,
                new PageOptions(PageSize: 20),
                "ELIGIBLE",
                null,
                CancellationToken.None,
                "VR-ELIGIBLE",
                "tripTerminalAt");
            var settlementItem = settlements.Items.Should().ContainSingle().Which;
            settlementItem.NetAmount.Should().Be(250, "the list must use live canonical ledger projection");
            settlementItem.FinancialBreakdown.Should().Be(
                new SettlementFinancialBreakdownDto(350, 300, 30, 20, 50, -30, 250));
            settlementItem.ProcessingState.Should().Be("RETRY_SCHEDULED");
            settlementItem.DelayReason.Should().Be("SYSTEM_PROCESSING_DELAY");
            settlementItem.DelayReason.Should().NotContain("PLATFORM_WALLET");
            settlementItem.AttemptCount.Should().Be(1);
            settlementItem.Trip.Should().NotBeNull();
            settlementItem.DataCompleteness.Should().Be("COMPLETE");

            var legacySettlementPage = await service.ListOperatorSettlementsAsync(
                operatorId,
                new PageOptions(PageSize: 20),
                "PENDING_HOLD",
                null,
                CancellationToken.None,
                legacyTripId.ToString());
            var legacySettlementItem = legacySettlementPage.Items.Should().ContainSingle().Which;
            legacySettlementItem.Trip.Should().NotBeNull();
            legacySettlementItem.DataCompleteness.Should().Be("PARTIAL",
                "legacy financial metadata is incomplete even when Trip enrichment succeeds");

            var sortedSettlements = await service.ListOperatorSettlementsAsync(
                operatorId,
                new PageOptions(PageSize: 2, SortBy: "netAmount", SortDir: "asc"),
                null,
                null,
                CancellationToken.None);
            sortedSettlements.Items.Select(item => item.TripId)
                .Should().Equal(legacyTripId, pendingTripId);

            var specialSearch = await service.ListOperatorLedgerAsync(
                operatorId,
                new PageOptions(PageSize: 20),
                null,
                null,
                null,
                CancellationToken.None,
                "100%_done\\ok",
                "occurredAt");
            specialSearch.Items.Should().ContainSingle()
                .Which.ReferenceCode.Should().Be("VR-ELIGIBLE");

            var legacy = await service.ListOperatorLedgerAsync(
                operatorId,
                new PageOptions(PageSize: 20),
                null,
                null,
                null,
                CancellationToken.None,
                legacyTripId.ToString(),
                "occurredAt");
            var legacyItem = legacy.Items.Should().ContainSingle().Which;
            legacyItem.DataCompleteness.Should().Be("PARTIAL");
            legacyItem.MissingFields.Should().Contain(["referenceCode", "occurredAt"]);
            legacyItem.OccurredAt.Should().Be(legacyItem.CreatedAt);
            legacyItem.OccurredAtSource.Should().Be("LEDGER_CREATED_AT_FALLBACK");

            var settlementTransactions = await service.ListOperatorTransactionsAsync(
                operatorId,
                new PageOptions(PageSize: 20),
                null,
                null,
                CancellationToken.None,
                settledTripId.ToString(),
                "createdAt");
            var settlementTransaction = settlementTransactions.Items.Should().ContainSingle().Which;
            settlementTransaction.SignedAmount.Should().Be(400);
            settlementTransaction.RelatedSettlement!.TripId.Should().Be(settledTripId);
            settlementTransaction.RelatedSettlement.Method.Should().Be("AUTO_WEEKLY");

            var adjustmentTransactions = await service.ListOperatorTransactionsAsync(
                operatorId,
                new PageOptions(PageSize: 20),
                null,
                "ADJUSTMENT",
                CancellationToken.None);
            var adjustment = adjustmentTransactions.Items.Should().ContainSingle().Which;
            adjustment.ActorType.Should().Be("USER");
            adjustment.Actor!.UserId.Should().Be(adminId);
            adjustment.AdjustmentReason.Should().Be("MANUAL_WALLET_ADJUSTMENT");

            trips.Fail = true;
            var partial = await service.ListOperatorSettlementsAsync(
                operatorId,
                new PageOptions(PageSize: 20),
                "ELIGIBLE",
                null,
                CancellationToken.None);
            var partialItem = partial.Items.Should().ContainSingle().Which;
            partialItem.Trip.Should().BeNull();
            partialItem.DataCompleteness.Should().Be("PARTIAL");

            var invalidSearch = () => service.ListOperatorLedgerAsync(
                operatorId,
                new PageOptions(),
                null,
                null,
                null,
                CancellationToken.None,
                "x");
            await invalidSearch.Should().ThrowAsync<BadRequestException>();
            var invalidDateField = () => service.ListOperatorSettlementsAsync(
                operatorId,
                new PageOptions(),
                null,
                null,
                CancellationToken.None,
                null,
                "unknownAt");
            await invalidDateField.Should().ThrowAsync<BadRequestException>();

            await VerifyMigrationDownAndUpAsync(db);
        }
        finally
        {
            var connectedDatabase = db.Database.GetDbConnection().Database;
            if (!databaseName.StartsWith(ScratchPrefix, StringComparison.Ordinal)
                || !string.Equals(connectedDatabase, databaseName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Refusing to delete non-scratch database '{connectedDatabase}'.");
            }
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task VerifyMigrationDownAndUpAsync(PaymentDbContext db)
    {
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260807085947_EnforceOperatorLedgerAdjustmentReason");
        (await MetadataColumnCountAsync(db)).Should().Be(0);
        await migrator.MigrateAsync();
        (await MetadataColumnCountAsync(db)).Should().Be(3);
        var constraintCount = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value"
            FROM information_schema.table_constraints
            WHERE table_schema = 'vietride_payment'
              AND table_name = 'operator_ledger_entries'
              AND constraint_name = 'chk_operator_ledger_entries_operator_funded_voucher_amount'
            """).SingleAsync();
        constraintCount.Should().Be(1);
    }

    private static Task<int> MetadataColumnCountAsync(PaymentDbContext db)
        => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'vietride_payment'
              AND table_name = 'operator_ledger_entries'
              AND column_name IN ('reference_code', 'occurred_at', 'operator_funded_voucher_amount')
            """).SingleAsync();

    private static OperatorLedgerEntry Revenue(
        Guid operatorId,
        Guid tripId,
        long amount,
        string code,
        DateTimeOffset occurredAt,
        string? note = null)
        => Entry(operatorId, tripId, OperatorLedgerEntryType.BOOKING_REVENUE, amount, code, occurredAt, note);

    private static OperatorLedgerEntry LegacyRevenue(Guid operatorId, Guid tripId, long amount)
        => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.BOOKING_REVENUE,
            amount,
            OperatorLedgerReferenceType.BOOKING,
            Guid.NewGuid(),
            Guid.NewGuid());

    private static OperatorLedgerEntry Refund(
        Guid operatorId,
        Guid tripId,
        long amount,
        string code,
        DateTimeOffset occurredAt)
        => Entry(operatorId, tripId, OperatorLedgerEntryType.BOOKING_REFUND, amount, code, occurredAt);

    private static OperatorLedgerEntry VietRideVoucher(
        Guid operatorId,
        Guid tripId,
        long amount,
        string code,
        DateTimeOffset occurredAt)
        => Entry(operatorId, tripId, OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT, amount, code, occurredAt);

    private static OperatorLedgerEntry OperatorVoucher(
        Guid operatorId,
        Guid tripId,
        long amount,
        string code,
        DateTimeOffset occurredAt)
        => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT,
            0,
            OperatorLedgerReferenceType.BOOKING,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "operator-funded-voucher",
            referenceCode: code,
            occurredAt: occurredAt,
            operatorFundedVoucherAmount: amount);

    private static OperatorLedgerEntry VoucherReversal(
        Guid operatorId,
        Guid tripId,
        long amount,
        string code,
        DateTimeOffset occurredAt)
        => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.ADJUSTMENT,
            amount,
            OperatorLedgerReferenceType.BOOKING,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "reverse-vietride-funded-voucher",
            adjustmentReason: OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL,
            referenceCode: code,
            occurredAt: occurredAt);

    private static OperatorLedgerEntry Entry(
        Guid operatorId,
        Guid tripId,
        OperatorLedgerEntryType type,
        long amount,
        string code,
        DateTimeOffset occurredAt,
        string? note = null)
        => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            type,
            amount,
            OperatorLedgerReferenceType.BOOKING,
            Guid.NewGuid(),
            Guid.NewGuid(),
            note,
            referenceCode: code,
            occurredAt: occurredAt);

    private static TripRevenueSummaryItem Summary(Guid tripId, string routeName)
        => new(
            tripId,
            "COMPLETED",
            new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            routeName,
            "Ho Chi Minh City",
            "Da Lat");

    private static ServiceProvider CreateProvider(
        string connectionString,
        IClock clock,
        FakeTripClient trips)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        PaymentDbContext.ConfigurePostgresTypes(dataSourceBuilder);
        var dataSource = dataSourceBuilder.Build();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InvoiceStorage:Provider"] = "E2E_LOCAL",
                ["InvoiceStorage:StableBaseUrl"] = "https://payment.test",
                ["OperatorWeb:InvoiceDetailBaseUrl"] = "https://operator.test/invoices",
                ["Identity:BaseUrl"] = "http://identity.test",
                ["Trip:BaseUrl"] = "http://trip.test",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<IClock>(clock);
        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(dataSource));
        services.AddScoped<VietRideDbContextBase>(provider => provider.GetRequiredService<PaymentDbContext>());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IIntegrationEventOutbox, CapturingOutbox>();
        services.AddInfrastructure(configuration, registerConsumers: false);
        services.RemoveAll<IIdentityFinancialProjectionClient>();
        services.AddSingleton<IIdentityFinancialProjectionClient, EmptyIdentityClient>();
        services.RemoveAll<ITripRevenueAnalyticsClient>();
        services.AddSingleton<ITripRevenueAnalyticsClient>(trips);
        return services.BuildServiceProvider();
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

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class CapturingOutbox : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class EmptyIdentityClient : IIdentityFinancialProjectionClient
    {
        public Task<IReadOnlyList<IdentityFinancialOperator>> GetOperatorsAsync(
            IReadOnlyList<Guid> operatorIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IdentityFinancialOperator>>([]);

        public Task<IReadOnlyList<IdentityFinancialUser>> GetUsersAsync(
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IdentityFinancialUser>>([]);
    }

    private sealed class FakeTripClient(IReadOnlyList<TripRevenueSummaryItem> summaries)
        : ITripRevenueAnalyticsClient
    {
        public bool Fail { get; set; }

        public Task<IReadOnlyList<TripRevenueSummaryItem>> GetTripSummariesAsync(
            IReadOnlyList<Guid> tripIds,
            CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new HttpRequestException("Trip unavailable");
            return Task.FromResult<IReadOnlyList<TripRevenueSummaryItem>>(
                summaries.Where(item => tripIds.Contains(item.TripId)).ToArray());
        }

        public Task<IReadOnlyList<TripVehicleCountItem>> GetVehicleCountsAsync(
            IReadOnlyList<Guid> operatorIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TripVehicleCountItem>>([]);

        public Task<IReadOnlyList<TripRoutePerformanceItem>> GetRoutePerformanceAsync(
            Guid operatorId,
            string month,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TripRoutePerformanceItem>>([]);
    }
}
