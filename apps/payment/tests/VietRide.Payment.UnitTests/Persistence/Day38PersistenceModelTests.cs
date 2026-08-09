using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Persistence;

public sealed class Day38PersistenceModelTests
{
    [Fact]
    public void PaymentContext_IsJsonObjectAndImmutableAfterAssignment()
    {
        var payment = PaymentEntity.CreatePendingRedirect(
            PaymentReferenceType.BOOKING,
            Guid.NewGuid(),
            Money.FromRaw(125_001),
            PaymentMethod.VNPAY);

        payment.Context.Should().Be("{}");
        payment.AttachContext("{\"version\":1,\"allocations\":[]}");

        payment.Context.Should().Be("{\"version\":1,\"allocations\":[]}");
        payment.Invoking(x => x.AttachContext("{\"version\":2}"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Model_ContainsDay38TablesAndCanonicalUniqueIndexes()
    {
        using var context = CreateContext();

        context.Model.FindEntityType(typeof(Invoice))!.GetTableName().Should().Be("invoices");
        context.Model.FindEntityType(typeof(InvoiceNumberCounter))!.GetTableName().Should().Be("invoice_number_counters");
        context.Model.FindEntityType(typeof(OperatorWallet))!.GetTableName().Should().Be("operator_wallets");
        context.Model.FindEntityType(typeof(OperatorWalletTransaction))!.GetIndexes()
            .Single(x => x.GetDatabaseName() == "uq_operator_wallet_transactions_subscription")
            .IsUnique.Should().BeTrue();
        context.Model.FindEntityType(typeof(OperatorLedgerEntry))!.GetIndexes()
            .Single(x => x.GetDatabaseName() == "uq_operator_ledger_entries_source")
            .IsUnique.Should().BeTrue();
        var ledgerModel = context.Model.FindEntityType(typeof(OperatorLedgerEntry))!;
        ledgerModel.FindProperty(nameof(OperatorLedgerEntry.ReferenceCode))!.GetColumnName()
            .Should().Be("reference_code");
        ledgerModel.FindProperty(nameof(OperatorLedgerEntry.OccurredAt))!.GetColumnName()
            .Should().Be("occurred_at");
        ledgerModel.FindProperty(nameof(OperatorLedgerEntry.OperatorFundedVoucherAmount))!.GetColumnName()
            .Should().Be("operator_funded_voucher_amount");
        ledgerModel.GetIndexes().Should().Contain(index =>
            index.GetDatabaseName() == "idx_operator_ledger_entries_operator_reference_code");
        ledgerModel.GetIndexes().Should().Contain(index =>
            index.GetDatabaseName() == "idx_operator_ledger_entries_operator_occurred_at");
        context.Model.FindEntityType(typeof(OperatorTripSettlement))!.GetIndexes()
            .Single(x => x.GetDatabaseName() == "uq_operator_trip_settlements_operator_trip")
            .IsUnique.Should().BeTrue();
        context.Model.FindEntityType(typeof(ProcessedIntegrationEvent))!.GetIndexes()
            .Single(x => x.GetDatabaseName() == "uq_processed_integration_events_consumer_event")
            .IsUnique.Should().BeTrue();

        context.Model.FindEntityType(typeof(OperatorWallet))!.GetForeignKeys().Should().BeEmpty(
            "operatorId is a logical cross-database reference");
    }

    [Fact]
    public void OperatorLedgerFactory_EnforcesSignedDirectionAndAuditOnlyZero()
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.BOOKING_REFUND,
            -10_001,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId).Amount.Should().Be(-10_001);

        Action invalidRefund = () => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.BOOKING_REFUND,
            10_001,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId);
        invalidRefund.Should().Throw<ArgumentOutOfRangeException>();

        Action invalidAudit = () => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT,
            1,
            OperatorLedgerReferenceType.VOUCHER_USAGE,
            referenceId,
            eventId);
        invalidAudit.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OperatorLedgerFactory_EnforcesTypedAdjustmentSemantics()
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var reversal = OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.ADJUSTMENT,
            -10_001,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId,
            adjustmentReason: OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL);
        reversal.AdjustmentReason.Should().Be(
            OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL);

        Action missingReason = () => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.ADJUSTMENT,
            -1,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId);
        missingReason.Should().Throw<ArgumentException>();

        Action reasonOnRevenue = () => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.BOOKING_REVENUE,
            1,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId,
            adjustmentReason: OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT);
        reasonOnRevenue.Should().Throw<ArgumentException>();

        Action invalidReversal = () => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.ADJUSTMENT,
            1,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId,
            adjustmentReason: OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL);
        invalidReversal.Should().Throw<ArgumentException>();

        Action invalidGeneric = () => OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.ADJUSTMENT,
            -1,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId,
            adjustmentReason: OperatorLedgerAdjustmentReason.GENERIC_BOOKING_REFUND_ENTITLEMENT);
        invalidGeneric.Should().Throw<ArgumentException>();

        Action invalidManual = () => OperatorLedgerEntry.Create(
            operatorId,
            null,
            OperatorLedgerEntryType.ADJUSTMENT,
            1,
            OperatorLedgerReferenceType.BOOKING,
            referenceId,
            eventId,
            adjustmentReason: OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT);
        invalidManual.Should().Throw<ArgumentException>();

        Action legacy = () => OperatorLedgerEntry.Create(
            operatorId,
            null,
            OperatorLedgerEntryType.ADJUSTMENT,
            1,
            OperatorLedgerReferenceType.MANUAL,
            referenceId,
            eventId,
            adjustmentReason: OperatorLedgerAdjustmentReason.LEGACY_UNCLASSIFIED);
        legacy.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SettlementFactory_UsesSevenDayHoldWithoutRoundingMoney()
    {
        var terminalAt = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var settlement = OperatorTripSettlement.CreatePending(Guid.NewGuid(), Guid.NewGuid(), terminalAt);

        settlement.Status.Should().Be(OperatorTripSettlementStatus.PENDING_HOLD);
        settlement.EligibleAt.Should().Be(terminalAt.AddDays(7));
        Money.FromRaw(125_001).Amount.Should().Be(125_001);
    }

    private static PaymentDbContext CreateContext()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(
            "Host=localhost;Port=5432;Database=unused;Username=unused;Password=unused");
        PaymentDbContext.ConfigurePostgresTypes(dataSourceBuilder);
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(dataSourceBuilder.Build())
            .Options;
        return new PaymentDbContext(options, new SystemClock());
    }
}
